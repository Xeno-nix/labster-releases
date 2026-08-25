namespace CLMS.Web.Components.Pages.Inventory;

public class EquipmentItem
{
    public int EquipmentId { get; set; }
    public string EquipmentNo { get; set; } = "";
    public string? SerialNo { get; set; }
    public string EquipmentName { get; set; } = "";
    public string? Brand { get; set; }
    public string? ModelNumber { get; set; }
    public string EquipmentType { get; set; } = "computer_unit";
    public int LabId { get; set; }
    public string LabName { get; set; } = "";
    public string EquipmentStatus { get; set; } = "available";
    public int Quantity { get; set; } = 1;
    public bool PreventiveMaintenanceDone { get; set; }
    public bool CalibrationDone { get; set; }
    public string? Remarks { get; set; }
}

public class SupplyItem
{
    public int SupplyId { get; set; }
    public string SupplyName { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Unit { get; set; }
    public string Status { get; set; } = "in_stock";
    public string? Remarks { get; set; }
}

public record LabOption(int LabId, string LabName);

public static class InventoryLookups
{
    public static readonly string[] SupplyCategories =
    {
        "Office Supplies", "Cleaning Supplies", "Cables & Connectors", "Printing Supplies", "Tools", "Other"
    };

    public static readonly Dictionary<string, string> EquipmentTypeLabels = new()
    {
        ["computer_unit"] = "Computer Unit",
        ["peripheral"] = "Peripheral",
        ["component"] = "Component",
        ["miscellaneous"] = "Miscellaneous"
    };
}
