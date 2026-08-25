using CLMS.Application.Inventory;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CLMS.Web.Components.Pages.Inventory;

public partial class InventoryPrint : ComponentBase
{
    [Inject] private IInventoryService InventoryService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabFromQuery { get; set; }

    private string Tab => TabFromQuery == "supplies" ? "supplies" : "equipment";

    private List<EquipmentItem> EquipmentRows { get; set; } = new();
    private List<SupplyItem> SuppliesRows { get; set; } = new();

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["fully_stocked"] = "Fully Stocked",
        ["in_stock"] = "In Stock",
        ["low_stock"] = "Low Stock",
        ["out_of_stock"] = "Out of Stock",
    };

    protected override async Task OnInitializedAsync()
    {
        var equipment = await InventoryService.GetEquipmentAsync();
        var supplies = await InventoryService.GetSuppliesAsync();

        EquipmentRows = equipment.Select(MapEquipment).ToList();
        SuppliesRows = supplies.Select(MapSupply).ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("eval", "window.onload = () => window.print();");
        }
    }

    private Task Print() => JS.InvokeVoidAsync("print").AsTask();
    private Task ClosePage() => JS.InvokeVoidAsync("close").AsTask();

    private static string BrandModel(string? brand, string? model)
    {
        if (string.IsNullOrEmpty(brand) && string.IsNullOrEmpty(model)) return "—";
        return string.IsNullOrEmpty(brand) ? model! : $"{brand} / {model}";
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].Replace('-', ' ');

    private static string StatusLabel(string s) => StatusLabels.GetValueOrDefault(s, s);

    private static EquipmentItem MapEquipment(InventoryEquipmentDto dto) => new()
    {
        EquipmentId = dto.EquipmentId,
        EquipmentNo = dto.EquipmentNo,
        SerialNo = dto.SerialNo,
        EquipmentName = dto.EquipmentName,
        Brand = dto.Brand,
        ModelNumber = dto.ModelNumber,
        EquipmentType = dto.EquipmentType,
        LabId = dto.LabId,
        LabName = dto.LabName,
        EquipmentStatus = dto.EquipmentStatus,
        Quantity = dto.Quantity,
        PreventiveMaintenanceDone = dto.PreventiveMaintenanceDone,
        CalibrationDone = dto.CalibrationDone,
        Remarks = dto.Remarks
    };

    private static SupplyItem MapSupply(InventorySupplyDto dto) => new()
    {
        SupplyId = dto.SupplyId,
        SupplyName = dto.SupplyName,
        Category = dto.Category,
        Unit = dto.Unit,
        Status = dto.Status,
        Remarks = dto.Remarks
    };
}
