using CLMS.Domain.Common;

namespace CLMS.Domain.Entities.Booking;
public class StudentEnrollment : AuditableEntity
{
    public readonly new int Id;
    public readonly string StudentId;
    public readonly int ClassScheduleId;


}
