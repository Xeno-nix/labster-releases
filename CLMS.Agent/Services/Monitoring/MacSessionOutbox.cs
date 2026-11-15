using CLMS.Agent.Core;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Persists nonsecret observations in the user session until the service acknowledges them.</summary>
internal sealed class MacSessionOutbox
{
    public const int Capacity = 256;
    private readonly string path;
    private Snapshot state;
    private bool locked;
    public int Dropped { get; private set; }
    public IReadOnlyList<MacAccessEvent> Pending => state.Events.ToArray();

    /// <summary>Clears the in-memory overflow notice after a controlled diagnostic is emitted.</summary>
    public void ClearDropNotice() => Dropped = 0;

    /// <summary>Restores only the current boot/session outbox; a companion restart never assumes the screen remains locked.</summary>
    public MacSessionOutbox(string path, Guid bootId, int session)
    {
        if (bootId == Guid.Empty || session <= 0) throw new ArgumentException("A graphical boot/session identity is required.");
        this.path = path;
        var loaded = MonitoringFile.Read<Snapshot>(path);
        if (loaded is not null && (loaded.Version != 1 || loaded.Events is null || loaded.Events.Count > Capacity ||
            loaded.Events.Any(e => e is null || e.Id == Guid.Empty || e.BootId != loaded.BootId || e.AuditSessionId != loaded.AuditSessionId ||
                e.Kind is not ("Sign-in" or "Unlock")))) throw new InvalidDataException("Invalid session outbox.");
        state = loaded?.BootId == bootId && loaded.AuditSessionId == session ? loaded : new(1, bootId, session, false, []);
        MonitoringFile.Write(path, state);
    }

    /// <summary>Reports the first observation of a foreground graphical login once per audit session.</summary>
    public void ObserveConsole(bool isConsole, DateTimeOffset now)
    {
        if (!isConsole) { locked = false; return; }
        if (state.SignInObserved) return;
        Append("Sign-in", now, true);
    }

    /// <summary>Requires a foreground lock-to-unlock transition; wake or an initial unlocked state does not count as unlock.</summary>
    public void ObserveScreen(bool isLocked, bool isConsole, DateTimeOffset now)
    {
        ObserveConsole(isConsole, now);
        if (!isConsole) return;
        if (isLocked) { locked = true; return; }
        if (!locked) return;
        Append("Unlock", now, state.SignInObserved);
        locked = false;
    }

    /// <summary>Removes an acknowledged observation only after the acknowledgement is durably saved.</summary>
    public void Acknowledge(Guid id)
    {
        var next = state with { Events = state.Events.Where(e => e.Id != id).ToList() };
        MonitoringFile.Write(path, next);
        state = next;
    }

    /// <summary>Drops the oldest observation on overflow rather than allowing an unbounded offline queue.</summary>
    private void Append(string kind, DateTimeOffset now, bool signedIn)
    {
        var events = state.Events.Append(new MacAccessEvent(Guid.NewGuid(), kind, state.BootId, state.AuditSessionId, now.ToUniversalTime())).ToList();
        if (events.Count > Capacity) { events.RemoveAt(0); Dropped++; }
        var next = state with { Events = events, SignInObserved = signedIn };
        MonitoringFile.Write(path, next);
        state = next;
    }

    internal sealed record Snapshot(int Version, Guid BootId, int AuditSessionId, bool SignInObserved, List<MacAccessEvent> Events);
}
