using System.Collections.Concurrent;
using System.Threading.Channels;
using CLMS.Shared.Dtos;

namespace CLMS.Web.Services.Notifications;

/// <summary>Bounded, live-only delivery to explicitly addressed browser circuits. No public publishing endpoint.</summary>
public sealed class BrowserNotificationBus(TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, Subscription> subscriptions = new();

    public Subscription Subscribe(Guid userId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A user is required.");
        var id = Guid.NewGuid();
        var subscription = new Subscription(userId, () => subscriptions.TryRemove(id, out _));
        subscriptions[id] = subscription;
        return subscription;
    }

    public int Publish(SystemNotification notification, IReadOnlyCollection<Guid> userIds)
    {
        SystemNotificationProtocol.Validate(notification);
        if (notification.ExpiresAt <= clock.GetUtcNow()) return 0;
        var recipients = userIds.ToHashSet();
        var count = 0;
        foreach (var subscriber in subscriptions.Values)
            if (recipients.Contains(subscriber.UserId) && subscriber.Queue.Writer.TryWrite(notification)) count++;
        return count;
    }

    public sealed class Subscription(Guid userId, Action remove) : IDisposable
    {
        internal Guid UserId { get; } = userId;
        internal Channel<SystemNotification> Queue { get; } = Channel.CreateBounded<SystemNotification>(
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        public ChannelReader<SystemNotification> Reader => Queue.Reader;
        public void Dispose() { remove(); Queue.Writer.TryComplete(); }
    }
}
