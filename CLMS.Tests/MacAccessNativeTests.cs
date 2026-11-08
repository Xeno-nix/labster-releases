using System.Net.Sockets;
using System.Runtime.Versioning;
using CLMS.Agent.Service.Monitoring;
using Xunit;

namespace CLMS.Tests;

/// <summary>Opt-in checks for Darwin ABI and socket metadata on an actual Mac; no service state is read or modified.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacAccessNativeTests
{
    [NativeMacMonitoringFact]
    public void BootAndAuditSessionMetadataHaveValidNativeLayouts()
    {
        var first = MacAccessNative.Boot();
        var second = MacAccessNative.Boot();
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.InRange(first.StartedUtc, DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow.AddSeconds(30));
        Assert.True(MacAccessNative.Session().AuditSessionId > 0);
    }

    [NativeMacMonitoringFact]
    public async Task SocketAuditTokenMatchesTheSendingProcessAndRejectsNonGraphicalSessions()
    {
        var session = MacAccessNative.Session();
        Assert.True(session.UserId == 0, "Run native peer checks under sudo in an Aqua terminal, matching the root daemon's identity.");
        var directory = "/tmp/clms_mac_native_" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, "control.sock");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var accept = listener.AcceptAsync(timeout.Token);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token);
            using var peer = await accept;
            await client.SendAsync(new byte[] { 1 }, SocketFlags.None, timeout.Token);
            await peer.ReceiveAsync(new byte[1], SocketFlags.None, timeout.Token);
            if (session.IsGraphical)
            {
                var metadata = MacAccessNative.Peer(peer);
                Assert.Equal(session.UserId, metadata.UserId);
                Assert.Equal(session.AuditSessionId, metadata.AuditSessionId);
            }
            else Assert.Throws<UnauthorizedAccessException>(() => MacAccessNative.Peer(peer));
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) != "/tmp" || !Path.GetFileName(directory).StartsWith("clms_mac_native_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe native test cleanup path.");
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}

public sealed class NativeMacMonitoringFactAttribute : FactAttribute
{
    public NativeMacMonitoringFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("CLMS_MAC_MONITOR_NATIVE") != "1")
            Skip = "Set CLMS_MAC_MONITOR_NATIVE=1 on a Mac and run under sudo for native monitoring checks.";
    }
}
