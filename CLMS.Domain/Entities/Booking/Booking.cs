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

}
