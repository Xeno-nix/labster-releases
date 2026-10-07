namespace CLMS.Application.DTOs.Inventory;

public class EquipmentDTO
{
    public Guid Id { get; set; }

    public string EquipmentNo { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? ModelNumber { get; set; }
    public string EquipmentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool NeedsPm { get; set; }
    public bool NeedsCalibration { get; set; }
    public string? Remarks { get; set; }

    // Current assignment, denormalized for the list view.
    // Both pairs are null when the equipment isn't currently assigned anywhere.
    public Guid? CurrentComputerUnitId { get; set; }
    public string? CurrentComputerUnitName { get; set; }
    public int? CurrentLaboratoryId { get; set; }
    public string? CurrentLaboratoryName { get; set; }
}
