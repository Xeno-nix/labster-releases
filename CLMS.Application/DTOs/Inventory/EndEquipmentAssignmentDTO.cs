namespace CLMS.Application.DTOs.Inventory;

public class EndEquipmentAssignmentDTO
{
    // One of: "broken", "replaced", "moved", "returned", "lost", "other"
    public string EndReason { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}
