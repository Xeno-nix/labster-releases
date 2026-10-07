namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// A student on a class roster, as read from the students table.
/// <para>
/// The schema leaves all four name columns nullable, so every name property is nullable and
/// the UI renders an em dash instead of throwing on a NULL.
/// </para>
/// </summary>
public class EnrolledStudentDTO
{
    public string StudentId { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? MiddleName { get; set; }

    public string? Suffix { get; set; }

    /// <summary>Full name with whatever parts are present, e.g. "Juan D. Dela Cruz Jr."</summary>
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