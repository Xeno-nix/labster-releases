namespace CLMS.Application.DTOs.Booking;
public class UpsertClassScheduleDTO
{
    public int CourseId { get; set; }

    public Guid InstructorId { get; set; }

    public int LaboratoryId { get; set; }

    public int AcademicTermId { get; set; }

    /// <summary>
    /// Year level (1-5) and section group a class's weekly meetings together so a class
    /// list can be imported once and apply to every meeting. Nullable so existing
    /// schedules keep working; null either one makes the schedule a group of one.
    /// </summary>
    public int? YearLevel { get; set; }

    public string? Section { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly TimeStart { get; set; }

    public TimeOnly TimeEnd { get; set; }
}