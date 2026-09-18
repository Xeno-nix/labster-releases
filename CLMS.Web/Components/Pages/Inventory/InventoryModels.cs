namespace CLMS.Web.Components.Pages.Inventory;

public class EquipmentItem
{
    public Guid EquipmentId { get; set; }
    public string EquipmentNo { get; set; } = "";
    public string? SerialNo { get; set; }
    public string EquipmentName { get; set; } = "";
    public string? Brand { get; set; }
    public string? ModelNumber { get; set; }
    public string EquipmentType { get; set; } = "peripheral";
    public string EquipmentStatus { get; set; } = "available";
    public bool NeedsPm { get; set; }
    public bool NeedsCalibration { get; set; }
    public string? Remarks { get; set; }

    // Current assignment (read-only here - changed via Assign/EndAssignment, not a plain field edit)
    public Guid? CurrentComputerUnitId { get; set; }
    public string? CurrentComputerUnitName { get; set; }
    public int? CurrentLaboratoryId { get; set; }
    public string? CurrentLaboratoryName { get; set; }

    public string LocationLabel =>
        CurrentComputerUnitName is not null
            ? $"{CurrentComputerUnitName} ({CurrentLaboratoryName})"
            : CurrentLaboratoryName ?? "Unassigned";
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
        ["miscellaneous"] = "Miscellaneous",
    };

    // "broken" / "replaced" / "moved" / "returned" / "lost" / "other" — matches
    // AssignmentEndReason. Shown when ending a current assignment before reassigning.
    public static readonly Dictionary<string, string> AssignmentEndReasonLabels = new()
    {
        ["broken"] = "Broken",
        ["replaced"] = "Replaced",
        ["moved"] = "Moved",
        ["returned"] = "Returned",
        ["lost"] = "Lost",
        ["other"] = "Other",
    };
}
