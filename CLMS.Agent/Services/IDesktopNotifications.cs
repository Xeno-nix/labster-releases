using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

public interface IDesktopNotifications : IDisposable
{
    bool Enabled { get; }
    string Status { get; }
    void Initialize();
    Task<bool> SetEnabledAsync(bool enabled);
    Task<DesktopNotificationStatus> ShowAsync(SystemNotification notification);
}
