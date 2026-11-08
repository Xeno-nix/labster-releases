using System.Runtime.Versioning;
using CLMS.Agent.Core;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Runs in the Aqua user's session. Owns only an observation outbox, never device keys or authentication state.</summary>
[SupportedOSPlatform("macos")]
internal static class MacSessionCompanion
{
    /// <summary>Keeps Core Foundation on the main thread while UI prompts and IPC publishing run independently.</summary>
    public static void Run(CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var prompt = Task.Run(() => LoginCompanion.RunAsync(lifetime.Token));
        var stateGate = new object();
        MacSessionOutbox? outbox = null;
        var publisher = Task.Run(() => MacAccessPublisher.RunAsync(
            () => { lock (stateGate) return outbox?.Pending.FirstOrDefault(); },
            id => { lock (stateGate) outbox?.Acknowledge(id); },
            request => AgentIpc.RequestAsync(new("access-event", AccessEvent: request), lifetime.Token),
            () => MacSessionDiagnostics.Warning("macOS access event delivery was deferred; observations are retained where possible."), lifetime.Token));
        FileStream? processLock = null;
        MacScreenNotifications? notifications = null;
        var failures = 0;
        var nextAttempt = DateTimeOffset.MinValue;
        var nextConsoleCheck = DateTimeOffset.MinValue;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    if (notifications is null && now >= nextAttempt)
                    {
                        var session = MacAccessNative.Session();
                        if (session.UserId == 0 || !session.IsGraphical || session.AuditSessionId <= 0)
                            throw new InvalidOperationException("A graphical login session is required.");
                        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "CLMS", "Agent");
                        Directory.CreateDirectory(directory);
                        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                        if (processLock is null)
                        {
                            try { processLock = new FileStream(Path.Combine(directory, "session-monitor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                            catch (IOException) { MacSessionDiagnostics.Warning("A session companion is already running or its state is unavailable."); return; }
                        }
                        var boot = MacAccessNative.Boot();
                        lock (stateGate) outbox = new(Path.Combine(directory, "session-events.json"), boot.Id, session.AuditSessionId);
                        notifications = new(isLocked =>
                        {
                            try
                            {
                                lock (stateGate) outbox!.ObserveScreen(isLocked, MacAccessNative.IsConsoleUser(session.UserId), DateTimeOffset.UtcNow);
                            }
                            catch { MacSessionDiagnostics.Warning("macOS screen observation could not be saved."); }
                        });
                        failures = 0;
                        nextConsoleCheck = DateTimeOffset.MinValue;
                    }
                    if (notifications is not null)
                    {
                        if (now >= nextConsoleCheck)
                        {
                            var session = MacAccessNative.Session();
                            lock (stateGate)
                            {
                                outbox!.ObserveConsole(session.IsGraphical && MacAccessNative.IsConsoleUser(session.UserId), now);
                                if (outbox.Dropped > 0)
                                {
                                    MacSessionDiagnostics.Warning("The macOS session outbox reached its limit; oldest observations were dropped.");
                                    outbox.ClearDropNotice();
                                }
                            }
                            nextConsoleCheck = now.AddSeconds(1);
                        }
                        notifications.Poll();
                    }
                    else ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch
                {
                    notifications?.Dispose(); notifications = null;
                    nextAttempt = DateTimeOffset.UtcNow.Add(AgentCoordinator.ReconnectDelay(failures++));
                    MacSessionDiagnostics.Warning("macOS session monitoring is unavailable; check the Aqua companion and its private state directory. Retrying.");
                }
            }
        }
        finally
        {
            lifetime.Cancel();
            notifications?.Dispose();
            try { Task.WhenAll(prompt, publisher).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            processLock?.Dispose();
        }
    }
}

/// <summary>Retries observation delivery without coupling it to authentication or the native notification thread.</summary>
internal static class MacAccessPublisher
{
    /// <summary>Sends at most one event per second, retaining its ID through outages and lost acknowledgements.</summary>
    public static async Task RunAsync(Func<MacAccessEvent?> peek, Action<Guid> acknowledge,
        Func<MacAccessEvent, Task<IpcResponse>> send, Action warn, CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= (duration, token) => Task.Delay(duration, token);
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var retry = TimeSpan.FromSeconds(1);
            try
            {
                var observation = peek();
                if (observation is not null)
                {
                    var result = await send(observation);
                    if (result.Error is null || result.ErrorCode == "access_event_invalid")
                    {
                        if (result.Error is not null) warn();
                        acknowledge(observation.Id);
                        failures = 0;
                    }
                    else { warn(); retry = AgentCoordinator.ReconnectDelay(failures++); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { warn(); retry = AgentCoordinator.ReconnectDelay(failures++); }
            try { await delay(retry, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
