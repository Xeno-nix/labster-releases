namespace CLMS.Application.DTOs.Booking;
public class UpsertClassScheduleDTO
{
    public int CourseId { get; set; }

    public Guid InstructorId { get; set; }

    public int LaboratoryId { get; set; }

    public int AcademicTermId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly TimeStart { get; set; }

    public TimeOnly TimeEnd { get; set; }
}