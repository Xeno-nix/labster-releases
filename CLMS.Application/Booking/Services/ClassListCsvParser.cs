using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Common;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

namespace CLMS.Application.Services;

/// <summary>
/// Pure CSV reader for class-list imports. No I/O beyond the stream it is handed and no
/// database access, so it can be exercised in isolation (and later in a test project).
/// <para>
/// Limits, header aliases, whitespace normalisation and per-row validation all live here;
/// classifying a row against the students table and the class's roster is the service's job.
/// </para>
/// </summary>
public class ClassListCsvParser
{
    /// <summary>Largest accepted file, in bytes (1 MB).</summary>
    public const int MaxFileBytes = 1_048_576;

    /// <summary>Most data rows an import may contain (excluding the header).</summary>
    public const int MaxRows = 500;

    /// <summary>
    /// The accepted student id format, shared with ClassListService: YYYY-NNNNN.
    /// The digit run after the dash accepts 4 or 5 digits (e.g. 2026-0001 or 2024-00227).
    /// </summary>
    public const string StudentIdPattern = @"^\d{4}-\d{4,5}$";

    private const int MaxNameLength = 50;
    private const int MaxSuffixLength = 5;

    // Mirrors CLMS.Domain.ValueObjects.Name: letters, apostrophes, hyphens, single spaces.
    private static readonly Regex NamePattern = new(@"^[\p{L}'\-]+(\s[\p{L}'\-]+)*$", RegexOptions.Compiled);
    private static readonly Regex InternalWhitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Parses a UTF-8 CSV stream into validated rows. Throws a <see cref="DomainException"/>
    /// for files over the size/row limits or too malformed to read; row-level problems are
    /// reported as Invalid rows, never exceptions.
    /// </summary>
    public IReadOnlyList<ClassListImportRow> Parse(Stream csv)
    {
        if (csv.Length > MaxFileBytes)
            throw new DomainException($"The file is larger than the 1 MB limit.");

        var rows = new List<ClassListImportRow>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csvReader = new CsvReader(reader, BuildConfiguration());

        var rowNumber = 0;

        try
        {
            while (csvReader.Read())
            {
                if (++rowNumber > MaxRows)
                    throw new DomainException($"The file has more than {MaxRows} data rows. Split it and import in batches.");

                var record = csvReader.GetRecord<ClassListCsvRecord>();
                if (record is null)
                    break;

                rows.Add(NormalizeAndValidate(record, rowNumber, seenIds));
            }
        }
        catch (CsvHelperException ex)
        {
            throw new DomainException(
                $"Could not read the CSV at line {ex.Context?.Parser?.Row ?? rowNumber + 1}. Check for unclosed quotes or stray characters.");
        }

        return rows;
    }

    private static CsvConfiguration BuildConfiguration() =>
        new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            // A missing column header leaves the field empty; the row is validated per-line.
            MissingFieldFound = null,
            HeaderValidated = null
        };

    private static ClassListImportRow NormalizeAndValidate(
        ClassListCsvRecord record,
        int rowNumber,
        ISet<string> seenIds)
    {
        var row = new ClassListImportRow { RowNumber = rowNumber };

        // Uppercase and strip spaces so "2024- 0227" and "2024-0227" are the same id.
        var id = (record.StudentId ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);

        row.StudentId = id;
        row.FirstName = NormalizeName(record.FirstName);
        row.LastName = NormalizeName(record.LastName);
        row.MiddleName = NormalizeName(record.MiddleName);
        row.Suffix = (record.Suffix ?? string.Empty).Trim();

        var errors = new List<string>();

        if (id.Length == 0)
        {
            errors.Add("student_id (Student ID) is missing.");
        }
        else if (!Regex.IsMatch(id, StudentIdPattern))
        {
            errors.Add($"\"{id}\" is not a valid student ID. Expected YYYY-NNNNN (4 or 5 digits), e.g. 2026-0001 or 2024-00227.");
        }
        else if (!seenIds.Add(id))
        {
            errors.Add("Duplicate student ID in this file — the first row with this ID wins.");
        }

        ValidateName(row.FirstName, "First name", errors, required: true);
        ValidateName(row.LastName, "Last name", errors, required: true);
        ValidateName(row.MiddleName, "Middle name", errors, required: false);

        if (row.Suffix.Length > MaxSuffixLength)
            errors.Add($"Suffix \"{row.Suffix}\" is longer than {MaxSuffixLength} characters.");

        if (errors.Count > 0)
        {
            row.Status = ClassListImportStatus.Invalid;
            row.Error = string.Join(" ", errors);
        }

        return row;
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // A double space breaks the Name pattern — collapse whitespace runs to single spaces.
        return InternalWhitespace.Replace(value.Trim(), " ");
    }

    private static void ValidateName(string value, string label, ICollection<string> errors, bool required)
    {
        if (value.Length == 0)
        {
            if (required)
                errors.Add($"{label} is missing.");
            return;
        }

        if (value.Length > MaxNameLength)
        {
            errors.Add($"{label} must be {MaxNameLength} characters or fewer.");
            return;
        }

        if (!NamePattern.IsMatch(value))
        {
            if (value.Contains('.'))
                errors.Add($"{label} \"{value}\" contains periods. Only letters, apostrophes and hyphens are allowed — put \"Jr.\" in the suffix column instead of \"J.R.\".");
            else
                errors.Add($"{label} \"{value}\" can only contain letters, apostrophes, hyphens and single spaces.");
        }
    }

    // Header matching in CsvHelper lowercases both sides, so the aliases are case-insensitive.
    private sealed class ClassListCsvRecord
    {
        [Name("student_id", "Student ID", "ID Number")]
        public string StudentId { get; set; } = string.Empty;

        [Name("last_name", "Last Name", "Surname")]
        public string LastName { get; set; } = string.Empty;

        [Name("first_name", "First Name", "Given Name")]
        public string FirstName { get; set; } = string.Empty;

        [Name("middle_name", "Middle Name")]
        public string MiddleName { get; set; } = string.Empty;

        [Name("suffix")]
        public string Suffix { get; set; } = string.Empty;
    }
}