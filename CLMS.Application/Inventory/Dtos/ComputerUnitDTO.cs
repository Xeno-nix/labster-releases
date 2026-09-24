namespace CLMS.Application.DTOs.Inventory;

public class ComputerUnitDTO
{
    public Guid Id { get; set; }

    public string UnitName { get; set; } = string.Empty;
    public byte UnitNumber { get; set; }

    public int LaboratoryId { get; set; }
    public string LaboratoryName { get; set; } = string.Empty;
}
