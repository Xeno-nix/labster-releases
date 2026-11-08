using CLMS.Agent.Core;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Keeps service-side monitoring isolated from the authentication coordinator.</summary>
public sealed class MacAccessMonitor
{
    private readonly MacAccessJournal journal;

    /// <summary>Defers all file and native access until monitoring actually runs.</summary>
    public MacAccessMonitor(ILogger<MacAccessMonitor> logger)
    {
        journal = new(Path.Combine(ServiceVault.StateDirectory, "mac-access-checkpoint.json"),
            (kind, time, source) => logger.LogInformation("Workstation access event {Kind} at {OccurredAtUtc}; source {Source}", kind, time, source));
    }

    /// <summary>Initializes boot monitoring without depending on a signed-in user or MAUI.</summary>
    public Task InitializeAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        return journal.InitializeAsync(MacAccessNative.Boot(), ct);
    }

    /// <summary>Accepts only preverified socket-peer observations and returns a controlled acknowledgement or rejection.</summary>
    internal Task<AccessReceipt> AcceptAsync(MacAccessEvent report, MacPeerInfo peer, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        return journal.AcceptAsync(report, peer, MacAccessNative.Boot(), DateTimeOffset.UtcNow, ct);
    }
}
