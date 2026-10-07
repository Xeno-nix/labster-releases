namespace CLMS.Application.DTOs.Booking;

public class ClassScheduleDTO
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;

    public Guid InstructorId { get; set; } 
    public string InstructorName { get; set; } = string.Empty;

    public int LaboratoryId { get; set; }
    public string LaboratoryName { get; set; } = string.Empty;

    public int AcademicTermId { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly TimeStart { get; set; }

    public TimeOnly TimeEnd { get; set; }

    /// <summary>
    /// Year level and section group the weekly meetings of one class together.
    /// Null until a schedule is assigned them; see ClassSchedule for details.
    /// </summary>
    public int? YearLevel { get; set; }

    public string? Section { get; set; }

    /// <summary>
    /// Number of students enrolled in this specific schedule row. Populated only by the
    /// queries that need it (GetByInstructorAsync / GetAllForTermAsync); 0 elsewhere.
    /// </summary>
    public int StudentCount { get; set; }
}