using CLMS.Shared.Dtos;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
#elif MACCATALYST
using UserNotifications;
#endif

namespace CLMS.Agent.Services;

public sealed class DesktopNotifications(IDesktopAgentService desktop, ILogger<DesktopNotifications> logger) : IDesktopNotifications
{
    private const string Preference = "agent.desktop-notifications";
    private bool initialized;
    public bool Enabled => Preferences.Default.Get(Preference, false);
    public string Status { get; private set; } = "Desktop notifications are off.";
#if MACCATALYST
    private NotificationDelegate? notificationDelegate;
#endif

    public void Initialize()
    {
        if (initialized) return;
        try
        {
#if WINDOWS
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            try { AppNotificationManager.Default.Register(); }
            catch { AppNotificationManager.Default.NotificationInvoked -= OnInvoked; throw; }
#elif MACCATALYST
            notificationDelegate = new NotificationDelegate(desktop);
            UNUserNotificationCenter.Current.Delegate = notificationDelegate;
#endif
            initialized = true;
            Status = Enabled ? "Desktop notifications enabled. OS notification settings apply." : "Desktop notifications are off.";
        }
        catch (Exception ex)
        {
            Status = "Desktop notifications unavailable. Check OS permissions and app installation.";
            logger.LogWarning("Notification initialization failed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    public Task<bool> SetEnabledAsync(bool enabled) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!enabled)
        {
            Preferences.Default.Set(Preference, false);
            Status = "Desktop notifications are off.";
            return false;
        }
        Initialize();
        if (!initialized) return false;
#if MACCATALYST
        var permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound,
            (allowed, error) => permission.TrySetResult(allowed && error is null));
        if (!await permission.Task)
        {
            Preferences.Default.Set(Preference, false);
            Status = "Allow CLMS notifications in System Settings, then enable them here.";
            return false;
        }
#elif WINDOWS
        if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
        {
            Preferences.Default.Set(Preference, false);
            Status = "Allow CLMS notifications in Windows Settings, then enable them here.";
            return false;
        }
#endif
        Preferences.Default.Set(Preference, true);
        Status = "Desktop notifications enabled. OS notification settings apply.";
        return true;
    });

    public Task<DesktopNotificationStatus> ShowAsync(SystemNotification notification) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        SystemNotificationProtocol.Validate(notification);
        if (notification.ExpiresAt <= DateTimeOffset.UtcNow) return DesktopNotificationStatus.Expired;
        if (!Enabled) return DesktopNotificationStatus.Disabled;
        Initialize();
        if (!initialized) return DesktopNotificationStatus.Failed;
        try
        {
#if WINDOWS
            if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
            {
                Status = "Notifications are blocked in Windows Settings.";
                return DesktopNotificationStatus.Disabled;
            }
            var toast = new AppNotificationBuilder().AddText(notification.Title).AddText(notification.Body).BuildNotification();
            toast.Tag = notification.Id.ToString("N");
            toast.Expiration = notification.ExpiresAt;
            AppNotificationManager.Default.Show(toast);
            Status = toast.Id == 0 ? "The notification could not be submitted." : "Notification submitted. Display depends on Windows notification settings.";
            return toast.Id == 0 ? DesktopNotificationStatus.Failed : DesktopNotificationStatus.Submitted;
#elif MACCATALYST
            using var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
            if (settings.AuthorizationStatus is not (UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional))
            {
                Status = "Allow CLMS notifications in System Settings.";
                return DesktopNotificationStatus.Disabled;
            }
            using var content = new UNMutableNotificationContent { Title = notification.Title, Body = notification.Body };
            using var request = UNNotificationRequest.FromIdentifier(notification.Id.ToString("N"), content, null);
            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
            Status = "Notification submitted. Display depends on macOS notification settings.";
            return DesktopNotificationStatus.Submitted;
#else
            await Task.CompletedTask;
            return DesktopNotificationStatus.Unsupported;
#endif
        }
        catch (Exception ex)
        {
            logger.LogWarning("Notification submission failed ({ExceptionType}).", ex.GetType().Name);
            Status = "Notification could not be submitted. Check OS notification settings.";
            return DesktopNotificationStatus.Failed;
        }
    });

#if WINDOWS
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        => MainThread.BeginInvokeOnMainThread(desktop.Show);
#elif MACCATALYST
    private sealed class NotificationDelegate(IDesktopAgentService desktop) : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler)
            => completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);

        public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            MainThread.BeginInvokeOnMainThread(desktop.Show);
            completionHandler();
        }
    }
#endif

    public void Dispose()
    {
        if (!initialized) return;
        try
        {
#if WINDOWS
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
#elif MACCATALYST
            UNUserNotificationCenter.Current.Delegate = null;
            notificationDelegate?.Dispose();
#endif
        }
        catch (Exception ex) { logger.LogWarning("Notification cleanup failed ({ExceptionType}).", ex.GetType().Name); }
        finally { initialized = false; }
    }
}
