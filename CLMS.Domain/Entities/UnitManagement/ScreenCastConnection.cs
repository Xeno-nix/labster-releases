using CLMS.Domain.Common;
using CLMS.Domain.Enums;
namespace CLMS.Domain.Entities.UnitManagement;
public class ScreenCastConnection: AuditableEntity
{   
    public Guid ScreenCastId { get; set; }
    public Guid CastSessionId { get; set; }
    public Guid ComputerUnitId{ get; set; }

    public Status Status { get; set; }

}
