namespace CLMS.Application.DTOs.Booking;

/// <summary>Outcome of committing a CSV import.</summary>
public class ClassListImportResult
{
    /// <summary>Students enrolled into the class (counts students, not slot rows — a two-slot class still reports 38).</summary>
    public int Enrolled { get; set; }

    /// <summary>Students rows created in the students table.</summary>
    public int CreatedStudents { get; set; }

    /// <summary>Rows skipped because the student was already on the roster.</summary>
    public int AlreadyEnrolled { get; set; }

    /// <summary>Rows skipped because they failed validation.</summary>
    public int Invalid { get; set; }

    /// <summary>Summary in the shape: "Enrolled 38, created 5 new students, skipped 4 already enrolled, 2 invalid."</summary>
    public string Message =>
        $"Enrolled {Enrolled}, created {CreatedStudents} new students, skipped {AlreadyEnrolled} already enrolled, {Invalid} invalid.";
}