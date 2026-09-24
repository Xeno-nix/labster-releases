namespace CLMS.Application.DTOs.Inventory;

public class UpsertEquipmentDTO
{
    public string? SerialNumber { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? ModelNumber { get; set; }
    public string EquipmentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool NeedsPm { get; set; }
    public bool NeedsCalibration { get; set; }
    public string? Remarks { get; set; }
}
