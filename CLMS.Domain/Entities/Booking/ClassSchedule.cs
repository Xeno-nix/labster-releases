using CLMS.Domain.Common;
namespace CLMS.Domain.Entities.Booking;
public class ClassSchedule : AuditableEntity
{
    public new readonly int Id;
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int InstructorId { get; set; }

    public Schedule[] Schedules { get; set; }

    public ClassSchedule(int id, Course course, int instructorId)
    {
        Id = id;
        CourseId = course.Id;
        Course = course;
        InstructorId = instructorId;
    }
}
