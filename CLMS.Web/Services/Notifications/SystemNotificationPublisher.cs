using CLMS.Application.UnitRegistry;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.Notifications;

public sealed record NotificationDeliveryReport(int BrowserConnections,
    IReadOnlyDictionary<Guid, DesktopNotificationStatus> Workstations);

/// <summary>Trusted server integration point. Callers must resolve authorized recipient IDs for their own feature.</summary>
public sealed class SystemNotificationPublisher(BrowserNotificationBus browsers, AgentPresenceStore presence,
    IUnitRegistryRepository registry, IHubContext<AgentConnectionHub> hub, TimeProvider clock)
{
    public async Task<NotificationDeliveryReport> PublishAsync(SystemNotification notification,
        IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> workstationIds, CancellationToken cancellationToken = default)
    {
        SystemNotificationProtocol.Validate(notification);
        if (notification.ExpiresAt <= clock.GetUtcNow() || notification.ExpiresAt > clock.GetUtcNow().AddMinutes(5))
            throw new ArgumentException("Notifications must expire within five minutes.");
        if (userIds.Count > 256 || workstationIds.Count > 256)
            throw new ArgumentException("Send to at most 256 users and 256 workstations at a time.");
        cancellationToken.ThrowIfCancellationRequested();
        var count = browsers.Publish(notification, userIds);
        var results = new Dictionary<Guid, DesktopNotificationStatus>();
        foreach (var id in workstationIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (notification.ExpiresAt <= clock.GetUtcNow()) { results[id] = DesktopNotificationStatus.Expired; continue; }
            var unit = presence.Snapshot().GetValueOrDefault(id);
            if (unit is not { Connected: true }) { results[id] = DesktopNotificationStatus.Offline; continue; }
            var credential = presence.Credential(id, unit.ConnectionId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                if (credential is null || !await registry.IsAuthorizedAsync(id, credential.Generation, credential.CredentialId, timeout.Token)
                    || presence.Credential(id, unit.ConnectionId) != credential)
                { results[id] = DesktopNotificationStatus.Unauthorized; continue; }
                var result = await hub.Clients.Client(unit.ConnectionId).InvokeAsync<DesktopNotificationResult>(
                    SystemNotificationProtocol.Receive, notification, timeout.Token);
                results[id] = result is not null && result.Id == notification.Id && Enum.IsDefined(result.Status)
                    ? result.Status : DesktopNotificationStatus.Failed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { results[id] = DesktopNotificationStatus.Failed; }
        }
        return new(count, results);
    }
}
