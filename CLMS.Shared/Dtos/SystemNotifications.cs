namespace CLMS.Shared.Dtos;

public enum NotificationSeverity { Information, Warning, Critical }
public enum DesktopNotificationStatus { Submitted, Disabled, Expired, Failed, Unsupported, Offline, Unauthorized }
public sealed record SystemNotification(Guid Id, string Title, string Body, NotificationSeverity Severity,
    DateTimeOffset ExpiresAt);
public sealed record DesktopNotificationResult(Guid Id, DesktopNotificationStatus Status);

public static class SystemNotificationProtocol
{
    public const string Receive = "ReceiveSystemNotification";
    public static void Validate(SystemNotification notification)
    {
        if (notification.Id == Guid.Empty || !Enum.IsDefined(notification.Severity)
            || string.IsNullOrWhiteSpace(notification.Title) || notification.Title.Length > 100
            || string.IsNullOrWhiteSpace(notification.Body) || notification.Body.Length > 1000)
            throw new ArgumentException("A notification needs an ID, severity, title (1–100 characters), and message (1–1000 characters).");
    }
}
