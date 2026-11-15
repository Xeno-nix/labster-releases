using CLMS.Domain.Common;
using CLMS.Domain.Enums;
namespace CLMS.Domain.Entities.UnitManagement;
public sealed class ScreenCastConnection
{   
    public int ScreenCastConnectionId { get; set; }
    public int CastSessionId { get; set; }
    public Guid ComputerUnitId{ get; set; }

    public string Status { get; set; } = "disconnected";
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

}
