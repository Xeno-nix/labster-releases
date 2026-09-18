namespace CLMS.Application.DTOs.Inventory;

public class EquipmentAssignmentDTO
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }
    public string EquipmentName { get; set; } = string.Empty;

    public Guid? ComputerUnitId { get; set; }
    public string? ComputerUnitName { get; set; }

    public int? LaboratoryId { get; set; }
    public string? LaboratoryName { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? EndReason { get; set; }
    public string? Remarks { get; set; }

    public DateTime AssignedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
