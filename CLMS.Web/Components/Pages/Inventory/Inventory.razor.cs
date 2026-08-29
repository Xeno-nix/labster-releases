using CLMS.Application.Inventory;
using Microsoft.AspNetCore.Components;

namespace CLMS.Web.Components.Pages.Inventory;

public partial class Inventory : ComponentBase
{
    [Inject] private IInventoryService InventoryService { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabFromQuery { get; set; }

    private List<EquipmentItem> Equipment { get; set; } = new();
    private List<SupplyItem> Supplies { get; set; } = new();
    private List<LabOption> Labs { get; set; } = new();
    private string? FlashSuccess { get; set; }

    private string ActiveTab { get; set; } = "supplies";

    private string EqSearch { get; set; } = "";
    private string EqTypeFilter { get; set; } = "all";
    private string EqLabFilter { get; set; } = "all";
    private string EqStatusFilter { get; set; } = "all";

    private List<EquipmentItem> FilteredEquipment => Equipment.Where(e =>
        (EqTypeFilter == "all" || e.EquipmentType == EqTypeFilter) &&
        (EqLabFilter == "all" || e.LabId.ToString() == EqLabFilter) &&
        (EqStatusFilter == "all" || e.EquipmentStatus == EqStatusFilter) &&
        (string.IsNullOrWhiteSpace(EqSearch) ||
            (e.SerialNo ?? "").Contains(EqSearch, StringComparison.OrdinalIgnoreCase) ||
            e.EquipmentNo.Contains(EqSearch, StringComparison.OrdinalIgnoreCase) ||
            e.EquipmentName.Contains(EqSearch, StringComparison.OrdinalIgnoreCase) ||
            (e.Brand ?? "").Contains(EqSearch, StringComparison.OrdinalIgnoreCase))
    ).ToList();

    private string SupSearch { get; set; } = "";
    private string SupCategoryFilter { get; set; } = "all";
    private string SupStatusFilter { get; set; } = "all";

    private List<SupplyItem> FilteredSupplies => Supplies.Where(s =>
        (SupCategoryFilter == "all" || s.Category == SupCategoryFilter) &&
        (SupStatusFilter == "all" || s.Status == SupStatusFilter) &&
        (string.IsNullOrWhiteSpace(SupSearch) ||
            s.SupplyName.Contains(SupSearch, StringComparison.OrdinalIgnoreCase))
    ).ToList();

    private bool ShowEquipmentForm { get; set; }
    private bool EditingEquipment { get; set; }
    private EquipmentItem EquipmentFormModel { get; set; } = new();

    private bool ShowDeleteEquipment { get; set; }
    private EquipmentItem? PendingDeleteEquipment { get; set; }

    private bool ShowQr { get; set; }
    private bool ShowScanner { get; set; }
    private EquipmentItem? SelectedEquipment { get; set; }
    private string ScannerCode { get; set; } = string.Empty;

    private bool ShowSupplyForm { get; set; }
    private bool EditingSupply { get; set; }
    private SupplyItem SupplyFormModel { get; set; } = new();

    private bool ShowDeleteSupply { get; set; }
    private SupplyItem? PendingDeleteSupply { get; set; }

    private bool ShowStatusConfirm { get; set; }
    private SupplyItem? PendingStatusSupply { get; set; }
    private string? PendingStatusValue { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(TabFromQuery) && (TabFromQuery == "equipment" || TabFromQuery == "supplies"))
        {
            ActiveTab = TabFromQuery;
        }

        var equipmentTask = InventoryService.GetEquipmentAsync();
        var suppliesTask = InventoryService.GetSuppliesAsync();
        var labsTask = InventoryService.GetLabsAsync();

        await Task.WhenAll(equipmentTask, suppliesTask, labsTask);

        Equipment = equipmentTask.Result.Select(MapEquipment).ToList();
        Supplies = suppliesTask.Result.Select(MapSupply).ToList();
        Labs = labsTask.Result.Select(x => new LabOption(x.LabId, x.LabName)).ToList();
    }

    private void OpenAddEquipment()
    {
        EquipmentFormModel = new EquipmentItem { LabId = Labs.FirstOrDefault()?.LabId ?? 0 };
        EditingEquipment = false;
        ShowEquipmentForm = true;
    }

    private void OpenEditEquipment(EquipmentItem item)
    {
        EquipmentFormModel = CloneEquipment(item);
        EditingEquipment = true;
        ShowEquipmentForm = true;
    }

    private void CloseEquipmentForm() => ShowEquipmentForm = false;

    private async Task SaveEquipment(EquipmentItem model)
    {
        var dto = new InventoryEquipmentDto(
            model.EquipmentId,
            model.EquipmentNo,
            model.SerialNo,
            model.EquipmentName,
            model.Brand,
            model.ModelNumber,
            model.EquipmentType,
            model.LabId,
            model.LabName,
            model.EquipmentStatus,
            model.Quantity,
            model.PreventiveMaintenanceDone,
            model.CalibrationDone,
            model.Remarks);

        await InventoryService.CreateOrUpdateEquipmentAsync(dto);
        Equipment = (await InventoryService.GetEquipmentAsync()).Select(MapEquipment).ToList();
        FlashSuccess = EditingEquipment ? "Equipment updated successfully." : "Equipment added successfully.";

        ShowEquipmentForm = false;
    }

    private void OpenDeleteEquipment(EquipmentItem item)
    {
        PendingDeleteEquipment = item;
        ShowDeleteEquipment = true;
    }

    private async Task DeleteEquipmentConfirmed()
    {
        if (PendingDeleteEquipment is not null)
        {
            await InventoryService.DeleteEquipmentAsync(PendingDeleteEquipment.EquipmentId);
            Equipment = (await InventoryService.GetEquipmentAsync()).Select(MapEquipment).ToList();
            FlashSuccess = "Equipment deleted.";
        }

        ShowDeleteEquipment = false;
        PendingDeleteEquipment = null;
    }

    private void OpenQr(EquipmentItem item)
    {
        SelectedEquipment = item;
        ShowQr = true;
    }

    private void CloseQr()
    {
        ShowQr = false;
        SelectedEquipment = null;
    }

    private void OpenScanner()
    {
        ShowScanner = true;
        ScannerCode = string.Empty;
    }

    private void CloseScanner()
    {
        ShowScanner = false;
        ScannerCode = string.Empty;
    }

    private void ScanEquipmentCode()
    {
        var query = ScannerCode.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            FlashSuccess = "Enter an equipment number or serial to scan.";
            return;
        }

        var match = Equipment.FirstOrDefault(item =>
            item.EquipmentNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (item.SerialNo ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            FlashSuccess = "No matching equipment was found for that scan.";
            return;
        }

        SelectedEquipment = match;
        ShowScanner = false;
        ShowQr = true;
        ScannerCode = string.Empty;
    }

    private void OpenAddSupply()
    {
        SupplyFormModel = new SupplyItem();
        EditingSupply = false;
        ShowSupplyForm = true;
    }

    private void OpenEditSupply(SupplyItem item)
    {
        SupplyFormModel = CloneSupply(item);
        EditingSupply = true;
        ShowSupplyForm = true;
    }

    private void CloseSupplyForm() => ShowSupplyForm = false;

    private async Task SaveSupply(SupplyItem model)
    {
        var dto = new InventorySupplyDto(
            model.SupplyId,
            model.SupplyName,
            model.Category,
            model.Unit,
            model.Status,
            model.Remarks);

        await InventoryService.CreateOrUpdateSupplyAsync(dto);
        Supplies = (await InventoryService.GetSuppliesAsync()).Select(MapSupply).ToList();
        FlashSuccess = EditingSupply ? "Supply updated successfully." : "Supply added successfully.";

        ShowSupplyForm = false;
    }

    private void OpenDeleteSupply(SupplyItem item)
    {
        PendingDeleteSupply = item;
        ShowDeleteSupply = true;
    }

    private async Task DeleteSupplyConfirmed()
    {
        if (PendingDeleteSupply is not null)
        {
            await InventoryService.DeleteSupplyAsync(PendingDeleteSupply.SupplyId);
            Supplies = (await InventoryService.GetSuppliesAsync()).Select(MapSupply).ToList();
            FlashSuccess = "Supply deleted.";
        }

        ShowDeleteSupply = false;
        PendingDeleteSupply = null;
    }

    private void ConfirmStatusChange(SupplyItem item, string newStatus)
    {
        if (newStatus == item.Status) return;
        PendingStatusSupply = item;
        PendingStatusValue = newStatus;
        ShowStatusConfirm = true;
    }

    private async Task StatusChangeConfirmed()
    {
        if (PendingStatusSupply is not null && PendingStatusValue is not null)
        {
            await InventoryService.UpdateSupplyStatusAsync(PendingStatusSupply.SupplyId, PendingStatusValue);
            PendingStatusSupply.Status = PendingStatusValue;
            FlashSuccess = "Supply status updated.";
        }

        ShowStatusConfirm = false;
        PendingStatusSupply = null;
        PendingStatusValue = null;
    }

    private static EquipmentItem CloneEquipment(EquipmentItem item) => new()
    {
        EquipmentId = item.EquipmentId,
        EquipmentNo = item.EquipmentNo,
        SerialNo = item.SerialNo,
        EquipmentName = item.EquipmentName,
        Brand = item.Brand,
        ModelNumber = item.ModelNumber,
        EquipmentType = item.EquipmentType,
        LabId = item.LabId,
        LabName = item.LabName,
        EquipmentStatus = item.EquipmentStatus,
        Quantity = item.Quantity,
        PreventiveMaintenanceDone = item.PreventiveMaintenanceDone,
        CalibrationDone = item.CalibrationDone,
        Remarks = item.Remarks
    };

    private static SupplyItem CloneSupply(SupplyItem item) => new()
    {
        SupplyId = item.SupplyId,
        SupplyName = item.SupplyName,
        Category = item.Category,
        Unit = item.Unit,
        Status = item.Status,
        Remarks = item.Remarks
    };

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
