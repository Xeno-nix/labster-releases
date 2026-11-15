using System.Text.Json;
using CLMS.Agent.Core;
using CLMS.Agent.Service.Monitoring;
using Xunit;

namespace CLMS.Tests;

public sealed class MacAccessMonitoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task KernelBootIsRecordedOnceAcrossServiceRestartsAndClockChanges()
    {
        using var f = new Fixture();
        await f.Journal().InitializeAsync(f.Boot, default);
        await f.Journal().InitializeAsync(f.Boot with { StartedUtc = f.Boot.StartedUtc.AddMinutes(5) }, default);
        Assert.Equal(new[] { ("Startup", f.Boot.StartedUtc, "KernelBootTime") }, f.Events);
        var nextBoot = new MacBootInfo(Guid.NewGuid(), Now);
        await f.Journal().InitializeAsync(nextBoot, default);
        Assert.Equal(2, f.Events.Count);
        Assert.Equal(Now, f.Events[1].Time);
    }

    [Fact]
    public async Task LostAcknowledgementAndBothProcessRestartsDoNotRepeatSignIn()
    {
        using var f = new Fixture();
        var outbox = f.Outbox(); outbox.ObserveConsole(true, Now);
        var report = Assert.Single(outbox.Pending);
        Assert.Equal(AccessReceipt.Accepted, await f.Journal().AcceptAsync(report, f.Peer, f.Boot, Now, default));
        var restarted = f.Outbox(); restarted.ObserveConsole(true, Now.AddSeconds(1));
        Assert.Equal(report, Assert.Single(restarted.Pending));
        Assert.Equal(AccessReceipt.Duplicate, await f.Journal().AcceptAsync(report, f.Peer, f.Boot, Now.AddSeconds(1), default));
        restarted.Acknowledge(report.Id);
        Assert.Empty(f.Outbox().Pending);
        Assert.Single(f.Events, e => e.Kind == "Sign-in");
        Assert.Contains(f.Events, e => e.Source == "DesktopSession/Observed/BestEffort");
    }

    [Fact]
    public void UnlockRequiresAnObservedForegroundLockAndDoesNotSurviveCompanionRestart()
    {
        using var f = new Fixture(); var box = f.Outbox();
        box.ObserveScreen(false, true, Now); // Initially unlocked is not an unlock event.
        box.ObserveConsole(true, Now); // Wake/console polling is not an unlock event.
        Assert.Single(box.Pending);
        box.ObserveScreen(true, true, Now);
        box.ObserveScreen(true, true, Now);
        box.ObserveScreen(false, true, Now.AddMinutes(1));
        box.ObserveScreen(false, true, Now.AddMinutes(1));
        Assert.Single(box.Pending, e => e.Kind == "Unlock");
        box.ObserveScreen(true, true, Now.AddMinutes(2));
        box = f.Outbox(); // Lock state is unknown after restart, not restored from history.
        box.ObserveScreen(false, true, Now.AddMinutes(3));
        Assert.Single(box.Pending, e => e.Kind == "Unlock");
    }

    [Fact]
    public async Task FastUserSwitchingDoesNotRepeatExistingSessionSignInOrUnlock()
    {
        using var f = new Fixture(); var box = f.Outbox();
        box.ObserveConsole(true, Now);
        box.ObserveScreen(true, true, Now);
        box.ObserveConsole(false, Now); // Session switches out; do not keep its old lock hint.
        box.ObserveScreen(false, false, Now);
        box.ObserveConsole(true, Now.AddMinutes(1));
        box.ObserveScreen(false, true, Now.AddMinutes(1));
        Assert.Single(box.Pending);
        var first = Assert.Single(box.Pending);
        var journal = f.Journal();
        await journal.AcceptAsync(first, f.Peer, f.Boot, Now, default);
        Assert.Equal(AccessReceipt.Duplicate, await journal.AcceptAsync(first with { Id = Guid.NewGuid() }, f.Peer, f.Boot, Now, default));
        var next = first with { Id = Guid.NewGuid(), AuditSessionId = first.AuditSessionId + 1 };
        Assert.Equal(AccessReceipt.Accepted, await journal.AcceptAsync(next, f.Peer with { AuditSessionId = next.AuditSessionId }, f.Boot, Now, default));
        Assert.Equal(2, f.Events.Count(e => e.Kind == "Sign-in"));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("session")]
    [InlineData("zero-session")]
    [InlineData("boot")]
    [InlineData("kind")]
    [InlineData("future")]
    [InlineData("before-boot")]
    [InlineData("id")]
    public async Task InvalidReportsCannotReachTheEventLog(string scenario)
    {
        using var f = new Fixture();
        var report = f.Report(); var peer = f.Peer;
        switch (scenario)
        {
            case "root": peer = peer with { UserId = 0 }; break;
            case "session": peer = peer with { AuditSessionId = report.AuditSessionId + 1 }; break;
            case "zero-session": peer = peer with { AuditSessionId = 0 }; break;
            case "boot": report = report with { BootId = Guid.NewGuid() }; break;
            case "kind": report = report with { Kind = "Startup\ninjected" }; break;
            case "future": report = report with { ObservedUtc = Now.AddMinutes(1) }; break;
            case "before-boot": report = report with { ObservedUtc = f.Boot.StartedUtc.AddTicks(-1) }; break;
            case "id": report = report with { Id = Guid.Empty }; break;
        }
        Assert.Equal(AccessReceipt.Invalid, await f.Journal().AcceptAsync(report, peer, f.Boot, Now, default));
        Assert.Empty(f.Events);
    }

    [Fact]
    public async Task InactiveConsoleReportIsDeferredAndCanBeRetriedWithTheSameId()
    {
        using var f = new Fixture(); var journal = f.Journal(); var report = f.Report();
        Assert.Equal(AccessReceipt.InactiveSession, await journal.AcceptAsync(report, f.Peer with { IsConsoleUser = false }, f.Boot, Now, default));
        Assert.Empty(f.Events);
        Assert.Equal(AccessReceipt.Accepted, await journal.AcceptAsync(report, f.Peer, f.Boot, Now, default));
        Assert.Equal(AccessReceipt.Duplicate, await journal.AcceptAsync(report, f.Peer, f.Boot, Now, default));
    }

    [Fact]
    public async Task ExcessiveRequestsAreBoundedAndResumeAfterTheWindow()
    {
        using var f = new Fixture(); var journal = f.Journal();
        for (var i = 0; i < 60; i++) Assert.Equal(AccessReceipt.Accepted, await journal.AcceptAsync(f.Report("Unlock"), f.Peer, f.Boot, Now, default));
        var report = f.Report("Unlock");
        Assert.Equal(AccessReceipt.RateLimited, await journal.AcceptAsync(report, f.Peer, f.Boot, Now, default));
        Assert.Equal(AccessReceipt.Accepted, await journal.AcceptAsync(report, f.Peer, f.Boot, Now.AddMinutes(1), default));
    }

    [Fact]
    public void OutboxIsBoundedAndOldBootOrSessionReportsAreDiscarded()
    {
        using var f = new Fixture(); var box = f.Outbox();
        box.ObserveConsole(true, Now);
        for (var i = 0; i < MacSessionOutbox.Capacity + 2; i++)
        { box.ObserveScreen(true, true, Now); box.ObserveScreen(false, true, Now); }
        Assert.Equal(MacSessionOutbox.Capacity, box.Pending.Count);
        Assert.Equal(3, box.Dropped);
        Assert.Equal(box.Pending, f.Outbox().Pending);
        var nextSession = new MacSessionOutbox(f.OutboxPath, f.Boot.Id, f.Peer.AuditSessionId + 1);
        Assert.Empty(nextSession.Pending);
        nextSession.ObserveConsole(true, Now);
        var nextBoot = new MacSessionOutbox(f.OutboxPath, Guid.NewGuid(), f.Peer.AuditSessionId + 1);
        Assert.Empty(nextBoot.Pending);
    }

    [Fact]
    public async Task CorruptOrOversizedCheckpointsFailVisiblyWithoutReplayingActivity()
    {
        using var f = new Fixture();
        File.WriteAllText(f.JournalPath, "invalid json");
        await Assert.ThrowsAsync<JsonException>(() => f.Journal().InitializeAsync(f.Boot, default));
        Assert.Empty(f.Events);
        File.WriteAllBytes(f.JournalPath, new byte[512 * 1024 + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal().InitializeAsync(f.Boot, default));
        File.WriteAllText(f.OutboxPath, "invalid json");
        Assert.Throws<JsonException>(() => f.Outbox());
    }

    [Fact]
    public void FailedAcknowledgementWriteRetainsTheDurableOutbox()
    {
        using var f = new Fixture(); var box = f.Outbox(); box.ObserveConsole(true, Now);
        var report = Assert.Single(box.Pending);
        Directory.CreateDirectory(f.OutboxPath + ".tmp"); // Prevent atomic snapshot creation without changing the real file.
        Assert.NotNull(Record.Exception(() => box.Acknowledge(report.Id)));
        Assert.Equal(report, Assert.Single(box.Pending));
        Directory.Delete(f.OutboxPath + ".tmp");
        Assert.Equal(report, Assert.Single(f.Outbox().Pending));
    }

    [Fact]
    public async Task ConcurrentBootAndReportOperationsShareOneCheckpoint()
    {
        using var f = new Fixture(); var journal = f.Journal(); var report = f.Report();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            await journal.InitializeAsync(f.Boot, default);
            return await journal.AcceptAsync(report, f.Peer, f.Boot, Now, default);
        })));
        Assert.Single(results, r => r == AccessReceipt.Accepted);
        Assert.Equal(15, results.Count(r => r == AccessReceipt.Duplicate));
        Assert.Single(f.Events, e => e.Kind == "Startup");
        Assert.Single(f.Events, e => e.Kind == "Sign-in");
    }

    [Fact]
    public async Task CancellationStopsMonitoringWithoutEmittingOrSending()
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Journal().InitializeAsync(f.Boot, stop.Token));
        Assert.Empty(f.Events);
    }

    [Fact]
    public void LocalObservationPayloadContainsNoUserNamesOrCredentials()
    {
        using var f = new Fixture();
        var report = f.Report();
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(report, JsonSerializer.Deserialize<MacAccessEvent>(json));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "clms_mac_monitor_test_" + Guid.NewGuid().ToString("N"));
        public readonly MacBootInfo Boot = new(Guid.NewGuid(), Now.AddHours(-1));
        public readonly MacPeerInfo Peer = new(501, 100001, true);
        public readonly List<(string Kind, DateTimeOffset Time, string Source)> Events = [];
        public string JournalPath => Path.Combine(directory, "checkpoint.json");
        public string OutboxPath => Path.Combine(directory, "outbox.json");
        public Fixture() { Directory.CreateDirectory(directory); }
        public MacAccessJournal Journal() => new(JournalPath, (kind, time, source) => Events.Add((kind, time, source)));
        public MacSessionOutbox Outbox() => new(OutboxPath, Boot.Id, Peer.AuditSessionId);
        public MacAccessEvent Report(string kind = "Sign-in") => new(Guid.NewGuid(), kind, Boot.Id, Peer.AuditSessionId, Now);
        public void Dispose()
        {
            var resolved = Path.GetFullPath(directory);
            var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(resolved) != expectedParent || !Path.GetFileName(resolved).StartsWith("clms_mac_monitor_test_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
