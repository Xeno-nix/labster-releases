using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Components;

namespace CLMS.Web.Components.Pages.Monitoring;

public abstract class MonitoringPageBase : ComponentBase, IDisposable
{
    [Inject] protected IUnitMonitoringRepository MonitoringRepository { get; set; } = default!;
    [Inject] protected ILogger<MonitoringPageBase> Logger { get; set; } = default!;
    [Inject] protected CLMS.Web.Services.AgentConnections.AgentPresenceStore AgentPresence { get; set; } = default!;

    private IReadOnlyList<UnitMonitoringDTO> LiveUnits = [];
    private IReadOnlyDictionary<Guid, UnitMonitoringStatus> PreviewStatuses = new Dictionary<Guid, UnitMonitoringStatus>();
    protected IReadOnlyList<UnitMonitoringDTO> Units => PreviewStatuses.Count == 0 ? LiveUnits
        : LiveUnits.Select(unit => PreviewStatuses.TryGetValue(unit.Id, out var status)
            ? CLMS.Web.Models.UnitAvailabilityPreview.Apply(unit, status, ObservedAtUtc) : unit).ToList();

    protected void SetPreviewStatuses(IReadOnlyDictionary<Guid, UnitMonitoringStatus> statuses)
    {
        PreviewStatuses = statuses;
        OnDataUpdated();
    }
    protected bool Loading { get; private set; }
    protected string? Error { get; private set; }
    protected DateTime? LastUpdatedUtc { get; private set; }
    protected DateTime ObservedAtUtc { get; private set; } = DateTime.UtcNow;
    private readonly CancellationTokenSource Lifetime = new();
    private bool Disposed;

    protected override Task OnInitializedAsync() => LoadAsync();

    protected async Task LoadAsync()
    {
        if (Loading || Disposed)
            return;
        Loading = true;
        try
        {
            await RefreshDetailsAsync(Lifetime.Token);
            var units = await MonitoringRepository.GetAllAsync(Lifetime.Token);
            if (Disposed)
                return;
            var presence = AgentPresence.Snapshot();
            LiveUnits = units.Select(unit => CLMS.Web.Models.UnitMonitoringPresence.Apply(unit,
                presence.GetValueOrDefault(unit.Id))).ToList();
            LastUpdatedUtc = DateTime.UtcNow;
            Error = null;
        }
        catch (OperationCanceledException) when (Disposed) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not load unit monitoring data.");
            Error = LastUpdatedUtc.HasValue
                ? "Could not refresh workstations. Showing the last loaded records; try Refresh again."
                : "Could not load workstations. Check the database connection and try Refresh again.";
        }
        finally
        {
            Loading = false;
            ObservedAtUtc = DateTime.UtcNow;
            OnDataUpdated();
        }
    }

    protected virtual Task RefreshDetailsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual void OnDataUpdated() { }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _ = RefreshLoopAsync();
        return Task.CompletedTask;
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var cancellationToken = Lifetime.Token;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await InvokeAsync(async () => { await LoadAsync(); if (!Disposed) StateHasChanged(); });
        }
        catch (OperationCanceledException) when (Disposed) { }
    }

    public void Dispose()
    {
        Disposed = true;
        Lifetime.Cancel();
        Lifetime.Dispose();
    }
}
