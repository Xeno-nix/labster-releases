using CLMS.Domain.Common;
using CLMS.Domain.ValueObjects;
namespace CLMS.Domain.Entities.Booking;
public class Booking : AuditableEntity
{
    public readonly int LaboratoryId;
    public string RequestorName { get; set; }
    public ContactNumber ContactNumber { get; set; }
    public string Purpose { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public Booking (
        Guid id,
        int labId,
        string requestorName,
        ContactNumber contactNumber,
        string purpose,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime
    )
    {
        _id = id;
        LaboratoryId = labId;
        RequestorName = requestorName;
        ContactNumber = contactNumber;
        Purpose = purpose;
        Date = date;
        StartTime = startTime;
        EndTime = endTime;
    }
}
