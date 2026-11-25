using CLMS.Agent.Services;
using CLMS.Shared.Dtos;
using CLMS.Web.Services.Notifications;
using Xunit;

namespace CLMS.Tests;

public sealed class SystemNotificationTests
{
    internal static SystemNotification Notification() => new(Guid.NewGuid(), "Workstation notice", "Example message",
        NotificationSeverity.Warning, DateTimeOffset.UtcNow.AddMinutes(1));

    [Fact]
    public void BrowserDeliveryIsIsolatedToExplicitRecipientsAndUnsubscribes()
    {
        var bus = new BrowserNotificationBus(TimeProvider.System);
        var user = Guid.NewGuid();
        using var intended = bus.Subscribe(user);
        using var other = bus.Subscribe(Guid.NewGuid());
        var message = Notification();
        Assert.Equal(1, bus.Publish(message, [user]));
        Assert.True(intended.Reader.TryRead(out var received));
        Assert.Equal(message, received);
        Assert.False(other.Reader.TryRead(out _));
        intended.Dispose();
        Assert.Equal(0, bus.Publish(Notification(), [user]));
    }

    [Fact]
    public void BrowserQueuesAreBoundedAndDropOldestWhenViewerIsSlow()
    {
        var bus = new BrowserNotificationBus(TimeProvider.System);
        var user = Guid.NewGuid();
        using var subscription = bus.Subscribe(user);
        var notifications = Enumerable.Range(0, 40).Select(_ => Notification()).ToArray();
        foreach (var notification in notifications) bus.Publish(notification, [user]);
        var received = new List<SystemNotification>();
        while (subscription.Reader.TryRead(out var notification)) received.Add(notification);
        Assert.Equal(notifications.Skip(8), received);
    }

    [Fact]
    public void InvalidOrExpiredNotificationsAreNotQueued()
    {
        var bus = new BrowserNotificationBus(TimeProvider.System);
        var user = Guid.NewGuid();
        using var subscription = bus.Subscribe(user);
        Assert.Equal(0, bus.Publish(Notification() with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, [user]));
        Assert.Throws<ArgumentException>(() => bus.Publish(Notification() with { Title = new string('x', 101) }, [user]));
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AgentDeduplicatesConcurrentDeliveryWithoutShowingTwice()
    {
        var desktop = new FakeDesktop();
        var receiver = new NotificationReceiver(desktop);
        var message = Notification();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => receiver.ReceiveAsync(message, default)));
        Assert.Equal(1, desktop.Calls);
        Assert.All(results, result => Assert.Equal(new DesktopNotificationResult(message.Id, DesktopNotificationStatus.Submitted), result));
    }

    [Fact]
    public async Task AgentRejectsExpiredNotificationsAndReportsDisabledDelivery()
    {
        var desktop = new FakeDesktop { Result = DesktopNotificationStatus.Disabled };
        var receiver = new NotificationReceiver(desktop);
        Assert.Equal(DesktopNotificationStatus.Expired,
            (await receiver.ReceiveAsync(Notification() with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, default)).Status);
        Assert.Equal(0, desktop.Calls);
        Assert.Equal(DesktopNotificationStatus.Disabled, (await receiver.ReceiveAsync(Notification(), default)).Status);
        Assert.Equal(1, desktop.Calls);
    }

    [Theory]
    [InlineData(DesktopNotificationStatus.Submitted, WorkstationCommandStatus.MessageReceived)]
    [InlineData(DesktopNotificationStatus.Disabled, WorkstationCommandStatus.NotificationsDisabled)]
    [InlineData(DesktopNotificationStatus.Expired, WorkstationCommandStatus.Expired)]
    [InlineData(DesktopNotificationStatus.Unsupported, WorkstationCommandStatus.Unsupported)]
    [InlineData(DesktopNotificationStatus.Failed, WorkstationCommandStatus.Failed)]
    public async Task SendMessageUsesTheSharedDesktopNotificationService(DesktopNotificationStatus delivery, WorkstationCommandStatus expected)
    {
        var desktop = new FakeDesktop { Result = delivery };
        var actions = new WorkstationActions(desktop);
        const string message = "Save your work. <This is plain text>";
        Assert.Equal(expected, await actions.ExecuteAsync(WorkstationCommandKind.SendMessage, message, default));
        Assert.Equal(1, desktop.Calls);
        Assert.NotNull(desktop.LastNotification);
        Assert.Equal("Administrator message", desktop.LastNotification.Title);
        Assert.Equal(message, desktop.LastNotification.Body);
        Assert.Equal(NotificationSeverity.Information, desktop.LastNotification.Severity);
    }

    [Fact]
    public async Task DuplicateMessageCommandSubmitsOnlyOneNativeNotification()
    {
        var desktop = new FakeDesktop();
        var handler = new WorkstationCommandHandler(new WorkstationActions(desktop));
        var command = new WorkstationCommand(Guid.NewGuid(), WorkstationCommandKind.SendMessage, "Class is ending.", DateTimeOffset.UtcNow.AddSeconds(20));
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => handler.ExecuteAsync(command, default)));
        Assert.All(results, result => Assert.Equal(WorkstationCommandStatus.MessageReceived, result.Status));
        Assert.Equal(1, desktop.Calls);
    }

    private sealed class FakeDesktop : IDesktopNotifications
    {
        public int Calls;
        public DesktopNotificationStatus Result = DesktopNotificationStatus.Submitted;
        public SystemNotification? LastNotification;
        public bool Enabled => true;
        public string Status => "Test";
        public void Initialize() { }
        public void Dispose() { }
        public Task<bool> SetEnabledAsync(bool enabled) => Task.FromResult(enabled);
        public Task<DesktopNotificationStatus> ShowAsync(SystemNotification notification)
        { Calls++; LastNotification = notification; return Task.FromResult(Result); }
    }
}
