using CLMS.Domain.Enums;

namespace CLMS.Domain.Entities.Inventory;

public class Supply
{
    public readonly int Id;
    public string Name { get; set; }
    public string Category { get; set; }
    public string Unit { get; set; }
    public SupplyStatus Status { get; set; }
    public string? Remarks { get; set; }

    public Supply(int id, string name, string category, string unit, SupplyStatus status, string? remarks)
    {
        Id = id;
        Name = name;
        Category = category;
        Unit = unit;
        Status = status;
        Remarks = remarks;
    }
}
