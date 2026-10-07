using CLMS.Domain.Common;
namespace CLMS.Domain.Entities.Booking;
public class ClassSchedule : AuditableEntity
{
    public new int Id { get; set; }
    public int CourseId { get; set; }
    public Guid InstructorId { get; set; }
    public int LaboratoryId { get; set; }
    public int AcademicTermId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly TimeStart { get; set; }
    public TimeOnly TimeEnd { get; set; }

    /// <summary>
    /// Year level (1-5) and section identify which "class" a weekly meeting belongs to.
    /// A class is a group of class_schedule rows sharing course, instructor, term,
    /// year level and section. Both are nullable so existing schedules keep working;
    /// a schedule with either one null is treated as a group of one.
    /// </summary>
    public int? YearLevel { get; set; }

    public string? Section { get; set; }
}
