using CLMS.Agent.Core;

namespace CLMS.Agent.Service.Monitoring;

internal sealed record MacBootInfo(Guid Id, DateTimeOffset StartedUtc);
internal sealed record MacPeerInfo(uint UserId, int AuditSessionId, bool IsConsoleUser);
internal enum AccessReceipt { Accepted, Duplicate, Invalid, InactiveSession, RateLimited }

/// <summary>Serializes service-side boot/session checkpointing. Input observations cannot alter device authentication.</summary>
internal sealed class MacAccessJournal(string path, Action<string, DateTimeOffset, string> emit)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Snapshot? state;
    private DateTimeOffset window;
    private int requests;

    /// <summary>Logs the kernel boot once, even when the service starts again during the same OS boot.</summary>
    public async Task InitializeAsync(MacBootInfo boot, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { InitializeCore(boot); }
        finally { gate.Release(); }
    }

    /// <summary>Validates a peer-bound report, suppresses duplicates, and persists before returning an acknowledgement.</summary>
    public async Task<AccessReceipt> AcceptAsync(MacAccessEvent report, MacPeerInfo peer, MacBootInfo boot, DateTimeOffset now, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (now < window || now - window >= TimeSpan.FromMinutes(1)) { window = now; requests = 0; }
            if (++requests > 60) return AccessReceipt.RateLimited;
            if (peer.UserId == 0 || peer.AuditSessionId <= 0 || report.AuditSessionId != peer.AuditSessionId ||
                report.Id == Guid.Empty || report.BootId != boot.Id || report.Kind is not ("Sign-in" or "Unlock") ||
                report.ObservedUtc < boot.StartedUtc || report.ObservedUtc > now.AddSeconds(30)) return AccessReceipt.Invalid;
            if (!peer.IsConsoleUser) return AccessReceipt.InactiveSession;
            InitializeCore(boot);
            if (state!.EventIds.Contains(report.Id) || report.Kind == "Sign-in" && state.SignedInSessions.Contains(peer.AuditSessionId))
                return AccessReceipt.Duplicate;
            // Keep memory/disk bounded even if a local session sends arbitrary identifiers.
            if (report.Kind == "Sign-in" && state.SignedInSessions.Count >= 4096) return AccessReceipt.RateLimited;
            var next = state with
            {
                EventIds = state.EventIds.Append(report.Id).TakeLast(1024).ToList(),
                SignedInSessions = report.Kind == "Sign-in" ? state.SignedInSessions.Append(peer.AuditSessionId).ToList() : state.SignedInSessions
            };
            emit(report.Kind, report.ObservedUtc.ToUniversalTime(), "SessionCompanion/Observed/BestEffort");
            MonitoringFile.Write(path, next);
            state = next;
            return AccessReceipt.Accepted;
        }
        finally { gate.Release(); }
    }

    /// <summary>Loads lazily so a monitoring storage failure cannot prevent the authentication host from starting.</summary>
    private void InitializeCore(MacBootInfo boot)
    {
        if (boot.Id == Guid.Empty) throw new InvalidDataException("Invalid boot identity.");
        var loaded = state ?? MonitoringFile.Read<Snapshot>(path);
        if (loaded is not null && (loaded.Version != 1 || loaded.EventIds is null || loaded.SignedInSessions is null ||
            loaded.EventIds.Count > 1024 || loaded.SignedInSessions.Count > 4096)) throw new InvalidDataException("Invalid monitoring checkpoint.");
        if (loaded?.BootId == boot.Id) { state = loaded; return; }
        var next = new Snapshot(1, boot.Id, [], []);
        emit("Startup", boot.StartedUtc.ToUniversalTime(), "KernelBootTime");
        MonitoringFile.Write(path, next);
        state = next;
    }

    internal sealed record Snapshot(int Version, Guid BootId, List<int> SignedInSessions, List<Guid> EventIds);
}
