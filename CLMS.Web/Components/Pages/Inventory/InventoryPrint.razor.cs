using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CLMS.Web.Components.Pages.Inventory;

public partial class InventoryPrint : ComponentBase
{
    [Inject] private EquipmentService EquipmentService { get; set; } = default!;
    [Inject] private SupplyService SupplyService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabFromQuery { get; set; }

    private string Tab => TabFromQuery == "supplies" ? "supplies" : "equipment";

    private List<EquipmentItem> EquipmentRows { get; set; } = new();
    private List<SupplyItem> SuppliesRows { get; set; } = new();

    private static readonly Dictionary<string, string> SupplyStatusLabels = new()
    {
        ["fully_stocked"] = "Fully Stocked",
        ["in_stock"] = "In Stock",
        ["low_stock"] = "Low Stock",
        ["out_of_stock"] = "Out of Stock",
    };

    protected override async Task OnInitializedAsync()
    {
        var equipment = await EquipmentService.GetAllAsync();
        var supplies = await SupplyService.GetAllAsync();

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
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].Replace('_', ' ');

    private static string SupplyStatusLabel(string status) =>
        SupplyStatusLabels.GetValueOrDefault(status, Capitalize(status));

    private static EquipmentItem MapEquipment(EquipmentDTO dto) => new()
    {
        EquipmentId = dto.Id,
        EquipmentNo = dto.EquipmentNo,
        SerialNo = dto.SerialNumber,
        EquipmentName = dto.EquipmentName,
        Brand = dto.Brand,
        ModelNumber = dto.ModelNumber,
        EquipmentType = dto.EquipmentType,
        EquipmentStatus = dto.Status,
        NeedsPm = dto.NeedsPm,
        NeedsCalibration = dto.NeedsCalibration,
        Remarks = dto.Remarks,
        CurrentComputerUnitId = dto.CurrentComputerUnitId,
        CurrentComputerUnitName = dto.CurrentComputerUnitName,
        CurrentLaboratoryId = dto.CurrentLaboratoryId,
        CurrentLaboratoryName = dto.CurrentLaboratoryName
    };

    private static SupplyItem MapSupply(SupplyDTO dto) => new()
    {
        SupplyId = dto.Id,
        SupplyName = dto.Name,
        Category = dto.Category,
        Unit = dto.Unit,
        Status = dto.Status,
        Remarks = dto.Remarks
    };
}
