namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// Input for adding a student to a class. If the student id is unknown to the students
/// table, FirstName and LastName are required and the student row is created on the fly.
/// </summary>
public class AddStudentDTO
{
    /// <summary>Must match the YYYY-NNNNN student id format (4 or 5 digits after the dash).</summary>
    public string StudentId { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public string? Suffix { get; set; }
}