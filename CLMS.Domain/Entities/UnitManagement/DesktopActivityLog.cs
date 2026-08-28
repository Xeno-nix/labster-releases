namespace CLMS.Domain.Entities.UnitManagement;
using CLMS.Domain.Enums;
using CLMS.Domain.Common;
internal class DesktopActivityLog: AuditableEntity

{
    public Guid DesktopActivityId { get; set; }
    public Guid EquipmentId { get; set; }
    public string StudentId { get; set; }
    public ActivityType ActivityType { get; set; }
    public string Description{ get; set; } 
    public Boolean IsResolved { get; set; }
    public DateTime DetectedAt { get; set; }
    public int ResolveBy {  get; set; }
    public DateTime ResolveAt { get; set; }
}
