using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Services;
using Microsoft.AspNetCore.Components;

namespace CLMS.Web.Components.Pages.Inventory;

public partial class InventoryPrint : ComponentBase
{
    [Inject] private EquipmentService EquipmentService { get; set; } = default!;
    [Inject] private SupplyService SupplyService { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabFromQuery { get; set; }

    private string Tab => TabFromQuery == "supplies" ? "supplies" : "equipment";
    private const int RowsPerPage = 12;
    private int RowCount => Tab == "equipment" ? EquipmentRows.Count : SuppliesRows.Count;
    private int PageCount => Math.Max(1, (RowCount + RowsPerPage - 1) / RowsPerPage);
    private string DocumentTitle => Tab == "equipment" ? "Masterlist of Equipment" : "Masterlist of Supplies";
    private string UpdatedDate { get; } = $"Updated as of: {DateTime.Now:MMMM dd, yyyy}";
    private bool Loaded { get; set; }

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
        Loaded = true;
    }

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
