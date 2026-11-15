using CLMS.Agent.Service;
using CLMS.Agent.Service.Monitoring;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Local observations live with the application, not the connection or settings page.</summary>
public sealed class AccessMonitoringService(ILogger<AccessMonitoringService> logger)
{
    private CancellationTokenSource? lifetime;
    private Task? worker;
    private readonly AccessEventLog events = new(Path.Combine(UserAgentVault.StateDirectory, "access-events.jsonl"));
    public string Status { get; private set; } = "Access monitoring is starting.";
#if MACCATALYST
    private MacScreenNotifications? notifications;
    private MacSessionOutbox? outbox;
    private MacAccessJournal? journal;
    private MacBootInfo? boot;
    private IDispatcherTimer? timer;
    private bool polling;
#endif

    public Task StartAsync()
    {
        if (lifetime is not null) return Task.CompletedTask;
        lifetime = new();
#if WINDOWS
        worker = Task.Run(async () =>
        {
            var ct = lifetime.Token;
            try
            {
                Directory.CreateDirectory(UserAgentVault.StateDirectory);
                var monitor = new WindowsAccessMonitor(Path.Combine(UserAgentVault.StateDirectory, "unit-access-checkpoint.json"),
                    e => Emit(e.Event, e.OccurredUtc, "WindowsEventLog"));
                monitor.Initialize();
                Status = "Windows access monitoring active while the app runs.";
                while (!ct.IsCancellationRequested)
                {
                    if (monitor.ReadAvailable() < 256) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch
            {
                Status = "Access monitoring unavailable. Check event-log read permissions and Audit Logon policy.";
                logger.LogWarning("Windows access monitoring unavailable; authentication and screen sharing remain active.");
            }
        });
#elif MACCATALYST
        timer = Microsoft.Maui.Controls.Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += OnMacTick;
        timer.Start();
#endif
        return Task.CompletedTask;
    }

#if MACCATALYST
    private async void OnMacTick(object? sender, EventArgs args)
    {
        if (polling || lifetime?.IsCancellationRequested != false) return;
        polling = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker = completion.Task;
        try
        {
            var ct = lifetime.Token;
            var session = MacAccessNative.Session();
            if (session.UserId == 0 || !session.IsGraphical || session.AuditSessionId <= 0)
                throw new InvalidOperationException("A graphical user session is required.");
            if (notifications is null)
            {
                var directory = UserAgentVault.StateDirectory;
                Directory.CreateDirectory(directory);
                boot = MacAccessNative.Boot();
                journal = new(Path.Combine(directory, "mac-access-checkpoint.json"),
                    Emit);
                await journal.InitializeAsync(boot, ct);
                outbox = new(Path.Combine(directory, "session-events.json"), boot.Id, session.AuditSessionId);
                // MAUI pumps the main CFRunLoop; never create a second companion process.
                notifications = new(locked =>
                {
                    try { outbox.ObserveScreen(locked, MacAccessNative.IsConsoleUser(session.UserId), DateTimeOffset.UtcNow); }
                    catch { Status = "A screen observation could not be saved."; }
                });
            }
            var active = MacAccessNative.IsConsoleUser(session.UserId);
            outbox!.ObserveConsole(active, DateTimeOffset.UtcNow);
            if (outbox.Pending.FirstOrDefault() is { } report)
            {
                var result = await journal!.AcceptAsync(report, new(session.UserId, session.AuditSessionId, active), boot!, DateTimeOffset.UtcNow, ct);
                if (result is AccessReceipt.Accepted or AccessReceipt.Duplicate or AccessReceipt.Invalid) outbox.Acknowledge(report.Id);
            }
            Status = "macOS access observations active; unlock detection is best effort.";
        }
        catch (OperationCanceledException) when (lifetime?.IsCancellationRequested != false) { }
        catch
        {
            notifications?.Dispose(); notifications = null;
            Status = "Access monitoring unavailable; authentication and screen sharing remain active.";
        }
        finally { polling = false; completion.TrySetResult(); }
    }
#endif

    public async Task StopAsync()
    {
        lifetime?.Cancel();
#if MACCATALYST
        timer?.Stop();
        if (timer is not null) timer.Tick -= OnMacTick;
        notifications?.Dispose(); notifications = null;
#endif
        if (worker is not null) await worker;
        lifetime?.Dispose(); lifetime = null; worker = null;
        Status = "Access monitoring stopped.";
    }

    private void Emit(string kind, DateTimeOffset time, string source)
    {
        events.Write(kind, time, source);
        logger.LogInformation("Workstation access event {Kind} at {OccurredAtUtc}; source {Source}", kind, time, source);
    }
}
