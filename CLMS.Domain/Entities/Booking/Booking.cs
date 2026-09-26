using CLMS.Domain.Common;
using CLMS.Domain.ValueObjects;
namespace CLMS.Domain.Entities.Booking;
public class Booking : AuditableEntity
{
    public new int Id { get; set; }
    public int LaboratoryId { get; set; }
    public string RequestorName { get; set; }
    public string Email { get; set; } 
    public string Purpose { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public Booking (
        int id,
        int labId,
        string requestorName,
        string email,
        string purpose,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime
    )
    {
        Id = id;
        LaboratoryId = labId;
        RequestorName = requestorName;
        Email = email;
        Purpose = purpose;
        Date = date;
        StartTime = startTime;
        EndTime = endTime;
    }

    public Booking()
    {
        
    }
}
