# Notifications and quick controls

## Quick controls

Open **Unit Monitoring → View all screens** as an active administrator and start the selected laboratory's monitoring session. Choose **Select units** in the toolbar, then check workstation tiles or choose **Select all online** for the laboratory, including other screen pages. Open **Commands** for Send message, Lock workstation, Restart, or Shut down. The command menu overlays the screen grid. Send message opens a small composer; command feedback is available through **Results** beside the menu. Offline units cannot be selected. Changing laboratories or ending the monitoring session clears selection.

**Send message** uses the same native desktop-notification service as other workstation alerts, with the title **Administrator message**. This applies to both individual-unit controls and Quick controls. Enable notifications in the agent and OS settings first; disabled notifications are reported to the sender. A successful result means submission to the OS, not proof that the recipient saw or read it.

Restart and Shut down confirm the selected count and target names. The target list and message are captured before sending, so a refresh does not retarget a running batch. Commands run one workstation at a time, with the existing current-admin and live device-authorization checks for every target. Results are reported individually; a failure on one unit does not prevent the next unit from being attempted. **Stop sending remaining commands** cancels the pending wait and skips the rest. It cannot undo an action already received by a workstation. Check any unconfirmed workstation before retrying a power command.

## Desktop notifications

The web dashboard and screen wall have a **Notifications** menu for Admin, SuperAdmin, and Faculty accounts. Choose **Enable desktop alerts** to opt in on that browser for that account. Incoming alerts also appear in the menu when desktop alerts are disabled or unavailable; the menu retains the latest 20 for that open window. Desktop alerts require a supported browser, a secure origin, browser permission, and compatible OS notification settings.

Opening the notification bell marks its current messages as seen and clears the unread badge without deleting them. Messages received while the menu is open are also marked seen. Clicking a browser desktop notification marks that message seen. Read state is stored per account in that browser (up to 200 notification IDs) and synchronized between open tabs; if browser storage is blocked, it lasts for the browser session. Message history still belongs to the open window and is not saved to the database.

The agent settings have **Enable desktop notifications** and **Disable desktop notifications** controls. The preference belongs to the signed-in OS user and defaults to off. Windows uses Windows App SDK app notifications. Mac Catalyst uses UserNotifications, including foreground presentation. Clicking an agent notification brings its settings window forward. No Windows-only script or PowerShell notification workaround is used.

Notifications are live delivery over the running web circuit and authenticated agent connection. Keep the dashboard open and workstation agents connected. There is no Web Push/APNs service or offline queue. Closed browsers and disconnected workstations do not receive missed alerts later. OS settings, Focus/Do Not Disturb, browser support, or permissions can suppress a banner even after successful submission. A submission or queue acknowledgement does not mean the user saw or read it.

Build and restart the web app and updated agents. No database migration or paid notification API is needed. On a Mac, build and test the signed Mac Catalyst application; Apple's notification permission dialog requires a signed app, including local Debug builds. Native Mac behavior cannot be validated by a Windows build.

## Integration point for other features

Activity detection is intentionally outside this feature. No disconnect, failed-login, or resource-usage detector has been added. A trusted server service can inject the scoped `CLMS.Web.Services.Notifications.SystemNotificationPublisher` and publish an event to explicit recipients:

```csharp
using CLMS.Shared.Dtos;
using CLMS.Web.Services.Notifications;

// Resolve recipients according to your feature's current authorization rules.
var notification = new SystemNotification(
    Guid.NewGuid(),
    "Workstation notice",
    "Lab 1 / PC 04: your feature's message here.",
    NotificationSeverity.Warning,
    DateTimeOffset.UtcNow.AddMinutes(1));

NotificationDeliveryReport delivery = await publisher.PublishAsync(
    notification,
    userIds: [authorizedStaffUserId],
    workstationIds: [affectedWorkstationId],
    cancellationToken);
```

Pass empty user or workstation lists to send to only one side. Human user IDs and workstation IDs are separate recipients; no automatic broadcast or audience inference occurs. The caller must enforce its feature's staff/laboratory access rules and choose which text is appropriate for workstation users. Do not include passwords, tokens, or other secrets in notification text that may be visible on a lock screen. Do not expose the publisher through an unauthenticated or unrestricted client endpoint.

The publisher checks current workstation credentials before delivery. It returns the number of active browser connections queued and a per-workstation result such as `Submitted`, `Disabled`, `Offline`, `Unauthorized`, or `Failed`. Browser counts are connections, not unique users or displayed banners. New producer code should handle these results and rate-limit/coalesce noisy events.

Titles are limited to 100 characters and bodies to 1,000. Severity is Information, Warning, or Critical. Notifications must expire within five minutes; reuse the same ID for retrying the same event. Native receivers and browser display handlers suppress recently seen IDs, and browser queues are bounded. Use a new ID for a genuinely new event. Text is displayed as text; it is not interpreted as HTML or a shell command.

## Verification

Portable tests cover explicit recipient isolation, bounded queues, expiry, malformed payload rejection, duplicate handling, and real SignalR delivery to a fake native receiver. JavaScript tests cover per-account opt-in, denied permissions, direct-click permission requests, expiry, duplicate suppression, and unsupported origins. Tests do not lock, restart, shut down, or display real native notifications on the test machine.

Manual release checks: select multiple online units and verify message delivery and per-unit feedback; confirm and cancel power dialogs on disposable test units; opt in on Windows and macOS and send an administrator message to a connected workstation; block OS/browser notifications and verify fallback/status feedback; publish to explicit staff and workstation recipients; verify another user's browser gets nothing. Test Mac permission, foreground notification display, and click behavior on a signed Mac build.

Platform references: [Windows App SDK notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet), [.NET Apple-platform notifications](https://learn.microsoft.com/en-us/dotnet/ios/app-fundamentals/user-notifications), [browser Notifications API](https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API).
