namespace CLMS.Domain.Entities.Inventory;
public class OfficeSupply
{
    public readonly int Id;
    public string Name { get; set; }
    public int Quantity { get; set; }
    public string Unit { get; set; }
    public string Description { get; set; }
    public OfficeSupply(int id, string name, int quantity, string unit, string description)
    {
        Id = id;
        Name = name;
        Quantity = quantity;
        Unit = unit;
        Description = description;
    }
}

