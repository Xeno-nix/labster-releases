using CLMS.Domain.Common;

namespace CLMS.Domain.Entities.Booking;
public class StudentEnrollment : AuditableEntity
{
    public readonly new int Id;
    public readonly string StudentId;
    public readonly int ClassScheduleId;

    public StudentEnrollment(int id, string studentId, int classScheduleId)
    {
        Id = id;
        StudentId = studentId;
        ClassScheduleId = classScheduleId;
    }

}
