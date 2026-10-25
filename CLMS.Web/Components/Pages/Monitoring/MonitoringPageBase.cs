using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Components;

namespace CLMS.Web.Components.Pages.Monitoring;

public abstract class MonitoringPageBase : ComponentBase, IDisposable
{
    [Inject] protected IUnitMonitoringRepository MonitoringRepository { get; set; } = default!;
    [Inject] protected ILogger<MonitoringPageBase> Logger { get; set; } = default!;

    protected IReadOnlyList<UnitMonitoringDTO> Units { get; private set; } = [];
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
            var units = await MonitoringRepository.GetAllAsync(Lifetime.Token);
            if (Disposed)
                return;
            Units = units;
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

    protected virtual void OnDataUpdated() { }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _ = RefreshLoopAsync();
        return Task.CompletedTask;
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
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
