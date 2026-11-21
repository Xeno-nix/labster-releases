using CLMS.Application.Monitoring;

namespace CLMS.Web.Services;

/// <summary>Queues reauthorized component queries over the existing Blazor Server SignalR circuits.</summary>
public sealed class MonitoringUpdates(ILogger<MonitoringUpdates> logger) : IAbnormalActivityNotifications
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action)handler)(); }
            catch (Exception ex) { logger.LogDebug(ex, "A monitoring viewer is no longer available."); }
        }
    }
}
