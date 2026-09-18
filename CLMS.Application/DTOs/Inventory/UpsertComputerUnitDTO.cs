namespace CLMS.Application.DTOs.Inventory;

public class UpsertComputerUnitDTO
{
    public string UnitName { get; set; } = string.Empty;
    public byte UnitNumber { get; set; }
    public int LaboratoryId { get; set; }
}
