using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

public sealed class NotificationReceiver(IDesktopNotifications desktop, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, DesktopNotificationResult> completed = [];
    private readonly Queue<Guid> order = [];

    public async Task<DesktopNotificationResult> ReceiveAsync(SystemNotification notification, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (completed.TryGetValue(notification.Id, out var previous)) return previous;
            DesktopNotificationStatus status;
            try
            {
                SystemNotificationProtocol.Validate(notification);
                status = notification.ExpiresAt <= time.GetUtcNow() || notification.ExpiresAt > time.GetUtcNow().AddMinutes(5)
                    ? DesktopNotificationStatus.Expired : await desktop.ShowAsync(notification);
            }
            catch { status = DesktopNotificationStatus.Failed; }
            var result = new DesktopNotificationResult(notification.Id, status);
            completed[notification.Id] = result;
            order.Enqueue(notification.Id);
            if (order.Count > 256) completed.Remove(order.Dequeue());
            return result;
        }
        finally { gate.Release(); }
    }
}
