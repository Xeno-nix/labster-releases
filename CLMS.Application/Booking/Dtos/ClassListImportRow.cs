namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// One row from an imported class list CSV after parsing and preview classification.
/// </summary>
public class ClassListImportRow
{
    /// <summary>1-based position of the row in the CSV (after the header).</summary>
    public int RowNumber { get; set; }

    public string StudentId { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? MiddleName { get; set; }

    public string? Suffix { get; set; }

    public ClassListImportStatus Status { get; set; }

    /// <summary>Human-readable reason when the row is invalid.</summary>
    public string? Error { get; set; }

    /// <summary>True when the id matches an existing student whose stored name differs from the file.</summary>
    public bool NameDiffers { get; set; }

    /// <summary>The stored name when <see cref="NameDiffers"/> is true (the record is never overwritten).</summary>
    public string? StoredName { get; set; }

    /// <summary>Full name from the import row, mirroring EnrolledStudentDTO.DisplayName.</summary>
    public string DisplayName
    {
        get
        {
            var middleInitial = string.IsNullOrWhiteSpace(MiddleName)
                ? null
                : $"{MiddleName[0].ToString().ToUpperInvariant()}.";

            var parts = new[] { FirstName, middleInitial, LastName, Suffix };
            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}