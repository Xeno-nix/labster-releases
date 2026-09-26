using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.RegularExpressions;
using CLMS.Application.Services;

namespace CLMS.Web.Components.Pages.Inventory;

public partial class Inventory : ComponentBase, IAsyncDisposable
{
    [Inject] private EquipmentService EquipmentService { get; set; } = default!;
    [Inject] private SupplyService SupplyService { get; set; } = default!;
    [Inject] private LaboratoryService LaboratoryService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private const string QrElementId = "qr-canvas";
    private const string ScannerVideoElementId = "scanner-video";
    private bool _pendingQrRender;
    private DotNetObjectReference<Inventory>? _selfRef;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabFromQuery { get; set; }

    private List<EquipmentItem> Equipment { get; set; } = new();
    private List<SupplyItem> Supplies { get; set; } = new();
    private List<LabOption> Labs { get; set; } = new();
    private string? FlashSuccess { get; set; }
    private string? FlashError { get; set; }

    private string ActiveTab { get; set; } = "equipment";

    private string EqSearch { get; set; } = "";
    private string EqTypeFilter { get; set; } = "all";
    private string EqLabFilter { get; set; } = "all";
    private string EqStatusFilter { get; set; } = "all";

    private List<EquipmentItem> FilteredEquipment => Equipment.Where(e =>
        (EqTypeFilter == "all" || e.EquipmentType == EqTypeFilter) &&
        (EqLabFilter == "all" || (e.CurrentLaboratoryId?.ToString() ?? "") == EqLabFilter) &&
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
    private string QrEditStatus { get; set; } = "available";
    private bool QrEditNeedsPm { get; set; }
    private bool QrEditNeedsCalibration { get; set; }
    private string QrEditRemarks { get; set; } = "";

    // Reassign-to-lab panel (see LocationLabel on EquipmentItem for the current location display)
    private int QrReassignLabId { get; set; }
    private string QrReassignEndReason { get; set; } = "moved";

    private string ScannerCode { get; set; } = string.Empty;
    private bool? ScannerHasCamera { get; set; } // null = checking, true = active, false = none found
    private string? ScannerError { get; set; }
    private bool _pendingCameraCheck;
    private bool _pendingCameraStart;

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

        await ReloadAllAsync();
    }

    private async Task ReloadAllAsync()
    {
        var equipmentTask = EquipmentService.GetAllAsync();
        var suppliesTask = SupplyService.GetAllAsync();
        var labsTask = LaboratoryService.GetAllAsync();

        await Task.WhenAll(equipmentTask, suppliesTask, labsTask);

        Equipment = equipmentTask.Result.Select(MapEquipment).ToList();
        Supplies = suppliesTask.Result.Select(MapSupply).ToList();
        Labs = labsTask.Result.Select(x => new LabOption(x.Id, x.Name)).ToList();
    }

    private async Task ReloadEquipmentAsync()
        => Equipment = (await EquipmentService.GetAllAsync()).Select(MapEquipment).ToList();

    private async Task ReloadSuppliesAsync()
        => Supplies = (await SupplyService.GetAllAsync()).Select(MapSupply).ToList();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await JS.InvokeVoidAsync("lucide.createIcons");
        if (_pendingQrRender && ShowQr && SelectedEquipment is not null)
        {
            _pendingQrRender = false;
            await JS.InvokeVoidAsync("clmsInventoryQr.render", QrElementId, BuildEquipmentQrPayload(SelectedEquipment));
        }

        if (_pendingCameraCheck && ShowScanner)
        {
            _pendingCameraCheck = false;

            var hasCamera = await JS.InvokeAsync<bool>("clmsInventoryScanner.hasCamera");
            ScannerHasCamera = hasCamera;

            if (hasCamera)
            {
                // Don't start the camera yet — the <video> element only exists in the
                // DOM once Blazor re-renders with ScannerHasCamera == true. Flag it for
                // the *next* OnAfterRenderAsync pass, which fires after that render.
                _pendingCameraStart = true;
            }
            else
            {
                ScannerError = "Unable to find a camera.";
            }

            StateHasChanged();
            return;
        }

        if (_pendingCameraStart && ShowScanner && ScannerHasCamera == true)
        {
            _pendingCameraStart = false;

            _selfRef ??= DotNetObjectReference.Create(this);
            var result = await JS.InvokeAsync<ScannerStartResult>(
                "clmsInventoryScanner.start", ScannerVideoElementId, _selfRef);

            if (!result.Ok)
            {
                // e.g. camera permission denied, or already in use by another app/tab.
                ScannerHasCamera = false;
                ScannerError = result.Error ?? "Unable to find a camera.";
                StateHasChanged();
            }
        }
    }

    private sealed class ScannerStartResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>Invoked from JS (inventory-scanner.js) whenever the camera decodes a QR code.</summary>
    [JSInvokable]
    public async Task OnQrScanned(string data)
    {
        await JS.InvokeVoidAsync("clmsInventoryScanner.stop");
        ResolveAndOpenFromScan(data);
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("clmsInventoryScanner.stop");
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone (e.g. browser tab closed) — nothing to clean up client-side.
        }
        catch (InvalidOperationException)
        {
            // Disposed during static prerendering, before the interactive circuit
            // (and JS interop) was ever attached — nothing to clean up client-side.
        }

        _selfRef?.Dispose();
    }

    private string BuildEquipmentQrPayload(EquipmentItem item)
    {
        // Encodes a URL so scanning opens the equipment record directly.
        // Add a matching "/inventory/equipment/{id}" route/page later if you
        // want the scan to deep-link straight to a detail view.
        return $"{Navigation.BaseUri}inventory/equipment/{item.EquipmentId}";
    }

    private async Task DownloadQr()
    {
        if (SelectedEquipment is null) return;
        await JS.InvokeVoidAsync("clmsInventoryQr.download", QrElementId, $"QR-{SelectedEquipment.EquipmentNo}.png");
    }

    private void OpenAddEquipment()
    {
        EquipmentFormModel = new EquipmentItem();
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
        var dto = new UpsertEquipmentDTO
        {
            SerialNumber = model.SerialNo,
            EquipmentName = model.EquipmentName,
            Brand = model.Brand,
            ModelNumber = model.ModelNumber,
            EquipmentType = model.EquipmentType,
            Status = model.EquipmentStatus,
            NeedsPm = model.NeedsPm,
            NeedsCalibration = model.NeedsCalibration,
            Remarks = model.Remarks
        };

        try
        {
            if (EditingEquipment)
            {
                await EquipmentService.UpdateAsync(model.EquipmentId, dto);
                FlashSuccess = "Equipment updated successfully.";
            }
            else
            {
                await EquipmentService.CreateAsync(dto);
                FlashSuccess = "Equipment added successfully. Assign it to a lab from its QR panel.";
            }

            await ReloadEquipmentAsync();
            ShowEquipmentForm = false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            FlashError = ex.Message;
        }
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
            await EquipmentService.DeleteAsync(PendingDeleteEquipment.EquipmentId);
            await ReloadEquipmentAsync();
            FlashSuccess = "Equipment deleted.";
        }

        ShowDeleteEquipment = false;
        PendingDeleteEquipment = null;
    }

    private void OpenQr(EquipmentItem item)
    {
        SelectedEquipment = item;
        LoadQrEditFields(item);
        ShowQr = true;
        _pendingQrRender = true;
    }

    private void LoadQrEditFields(EquipmentItem item)
    {
        QrEditStatus = item.EquipmentStatus;
        QrEditNeedsPm = item.NeedsPm;
        QrEditNeedsCalibration = item.NeedsCalibration;
        QrEditRemarks = item.Remarks ?? "";
        QrReassignLabId = item.CurrentLaboratoryId ?? Labs.FirstOrDefault()?.LabId ?? 0;
        QrReassignEndReason = "moved";
    }

    private async Task SaveQrEdit()
    {
        if (SelectedEquipment is null) return;

        var dto = new UpsertEquipmentDTO
        {
            SerialNumber = SelectedEquipment.SerialNo,
            EquipmentName = SelectedEquipment.EquipmentName,
            Brand = SelectedEquipment.Brand,
            ModelNumber = SelectedEquipment.ModelNumber,
            EquipmentType = SelectedEquipment.EquipmentType,
            Status = QrEditStatus,
            NeedsPm = QrEditNeedsPm,
            NeedsCalibration = QrEditNeedsCalibration,
            Remarks = string.IsNullOrWhiteSpace(QrEditRemarks) ? null : QrEditRemarks
        };

        try
        {
            await EquipmentService.UpdateAsync(SelectedEquipment.EquipmentId, dto);
            await ReloadEquipmentAsync();

            SelectedEquipment = Equipment.FirstOrDefault(e => e.EquipmentId == SelectedEquipment.EquipmentId);
            FlashSuccess = "Equipment record updated.";
            CloseQr();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            FlashError = ex.Message;
        }
    }

    /// <summary>
    /// Assigns (or reassigns) the currently-selected equipment to a lab directly.
    /// If it already has an active assignment somewhere, that assignment is ended
    /// first with the chosen reason before the new one is created.
    /// </summary>
    private async Task ReassignToLab()
    {
        if (SelectedEquipment is null) return;

        try
        {
            var isCurrentlyAssigned =
                SelectedEquipment.CurrentComputerUnitId is not null ||
                SelectedEquipment.CurrentLaboratoryId is not null;

            if (isCurrentlyAssigned)
            {
                await EquipmentService.EndCurrentAssignmentAsync(
                    SelectedEquipment.EquipmentId,
                    new EndEquipmentAssignmentDTO { EndReason = QrReassignEndReason });
            }

            await EquipmentService.AssignAsync(new AssignEquipmentDTO
            {
                EquipmentId = SelectedEquipment.EquipmentId,
                LaboratoryId = QrReassignLabId
            });

            await ReloadEquipmentAsync();
            SelectedEquipment = Equipment.FirstOrDefault(e => e.EquipmentId == SelectedEquipment.EquipmentId);
            FlashSuccess = "Equipment reassigned.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            FlashError = ex.Message;
        }
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
        ScannerError = null;
        ScannerHasCamera = null; // triggers the "checking..." state, resolved in OnAfterRenderAsync
        _pendingCameraCheck = true;
    }

    private async Task CloseScanner()
    {
        await JS.InvokeVoidAsync("clmsInventoryScanner.stop");
        ShowScanner = false;
        ScannerCode = string.Empty;
        ScannerHasCamera = null;
        ScannerError = null;
    }

    private void ScanEquipmentCode() => ResolveAndOpenFromScan(ScannerCode);

    private void ResolveAndOpenFromScan(string query)
    {
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            FlashSuccess = "No code detected. Try again or enter it manually.";
            return;
        }

        EquipmentItem? match = null;

        // Camera scans decode our QR payload URL (".../inventory/equipment/{guid}") —
        // try that first, then fall back to matching raw text (manual entry, or a
        // QR/serial label that just contains the equipment/serial number).
        var idMatch = Regex.Match(
            query,
            @"inventory/equipment/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");

        if (idMatch.Success && Guid.TryParse(idMatch.Groups[1].Value, out var id))
        {
            match = Equipment.FirstOrDefault(e => e.EquipmentId == id);
        }

        match ??= Equipment.FirstOrDefault(item =>
            item.EquipmentNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (item.SerialNo ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            FlashSuccess = "No matching equipment was found for that scan.";
            return;
        }

        SelectedEquipment = match;
        LoadQrEditFields(match);
        ShowScanner = false;
        ShowQr = true;
        _pendingQrRender = true;
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
        var dto = new UpsertSupplyDTO
        {
            Name = model.SupplyName,
            Category = model.Category,
            Unit = model.Unit ?? "",
            Status = model.Status,
            Remarks = model.Remarks
        };

        if (EditingSupply)
        {
            await SupplyService.UpdateAsync(model.SupplyId, dto);
        }
        else
        {
            await SupplyService.CreateAsync(dto);
        }

        await ReloadSuppliesAsync();
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
            await SupplyService.DeleteAsync(PendingDeleteSupply.SupplyId);
            await ReloadSuppliesAsync();
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
            await SupplyService.UpdateStatusAsync(PendingStatusSupply.SupplyId, PendingStatusValue);
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
        EquipmentStatus = item.EquipmentStatus,
        NeedsPm = item.NeedsPm,
        NeedsCalibration = item.NeedsCalibration,
        Remarks = item.Remarks,
        CurrentComputerUnitId = item.CurrentComputerUnitId,
        CurrentComputerUnitName = item.CurrentComputerUnitName,
        CurrentLaboratoryId = item.CurrentLaboratoryId,
        CurrentLaboratoryName = item.CurrentLaboratoryName
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
