using CLMS.Agent.Core;
using CLMS.Agent.Models;
using CLMS.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentRuntimeTests
{
    private static AgentRuntimeService Create(Identity identity, Transport transport) =>
        new(identity, transport, NullLogger<AgentRuntimeService>.Instance, TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task MissingEnrollmentPollsWithoutOpeningAnAgentConnection()
    {
        var identity = new Identity { Status = new("RegistrationRequired", "Enrollment required.") };
        var transport = new Transport();
        await using var runtime = Create(identity, transport);
        Assert.False(await runtime.StartAsync());
        await Until(() => identity.Ticks > 1);
        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task UnreadableVaultLeavesSettingsAvailable()
    {
        var identity = new Identity { StorageFails = true };
        var transport = new Transport();
        await using var runtime = Create(identity, transport);
        Assert.False(await runtime.StartAsync());
        Assert.Contains("protected storage", runtime.Status);
        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task FailedInitialConnectionAndLaterDisconnectAreRetried()
    {
        var identity = new Identity(); var transport = new Transport { Failures = 1 };
        await using var runtime = Create(identity, transport);
        Assert.True(await runtime.StartAsync());
        await Until(() => transport.Attempts >= 2 && transport.IsRunning);
        var count = transport.Attempts;
        transport.IsRunning = false;
        await Until(() => transport.Attempts > count && transport.IsRunning);
    }

    [Fact]
    public async Task PauseCancelsAnInFlightConnectionAndLeavesNoRetryLoop()
    {
        var transport = new Transport { Block = true };
        await using var runtime = Create(new(), transport);
        await runtime.StartAsync();
        await Until(() => transport.Attempts > 0);
        await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var count = transport.Attempts;
        await Task.Delay(60);
        Assert.Equal(count, transport.Attempts);
        Assert.False(transport.IsRunning);
        Assert.Contains("paused", runtime.Status);
    }

    [Fact]
    public async Task RestartUsesCurrentTokenAndDisconnectsPreviousConnection()
    {
        var identity = new Identity(); var transport = new Transport();
        await using var runtime = Create(identity, transport);
        await runtime.StartAsync();
        await Until(() => transport.IsRunning);
        identity.Token = "replacement-jwt";
        await runtime.StartAsync();
        await Until(() => transport.LastToken == "replacement-jwt");
        Assert.False(transport.ConnectedOverRunningConnection);
        Assert.True(transport.Disconnects >= 2);
    }

    [Fact]
    public async Task RecoveryRequiredDisconnectsAndDoesNotResumeCaptureTransport()
    {
        var identity = new Identity(); var transport = new Transport();
        await using var runtime = Create(identity, transport);
        await runtime.StartAsync();
        await Until(() => transport.IsRunning);
        identity.Status = identity.Status with { Phase = "RecoveryRequired", Message = "Revoked" };
        await Until(() => !transport.IsRunning);
        var count = transport.Attempts;
        await Task.Delay(60);
        Assert.Equal(count, transport.Attempts);
    }

    [Fact]
    public async Task DisposeStopsEnrollmentPollingAndDisconnects()
    {
        var identity = new Identity(); var transport = new Transport();
        var runtime = Create(identity, transport);
        await runtime.StartAsync();
        await Until(() => transport.IsRunning);
        await runtime.DisposeAsync();
        var ticks = identity.Ticks;
        await Task.Delay(60);
        Assert.Equal(ticks, identity.Ticks);
        Assert.False(transport.IsRunning);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    private sealed class Identity : IAgentIdentityRuntime
    {
        public AgentStatus Status { get; set; } = new("Connected", "Ready", "https://lab.example", ComputerUnitId: Guid.NewGuid());
        public string Token = "current-jwt";
        public int Ticks;
        public bool StorageFails;
        public TimeSpan NextDelay => TimeSpan.FromSeconds(30);
        public Task InitializeAsync(CancellationToken ct) => StorageFails ? throw new IOException() : Task.CompletedTask;
        public Task TickAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref Ticks); return Task.CompletedTask; }
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(Token);
    }
    private sealed class Transport : IAgentConnectionTransport
    {
        public bool IsRunning { get; set; }
        public AgentConnectionSnapshot Snapshot => new(IsRunning ? "Connected" : "Disconnected");
        public int Attempts, Disconnects, Failures;
        public bool Block, ConnectedOverRunningConnection;
        public string? LastToken;
        public async Task ConnectAsync(string serverAddress, string deviceKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            ConnectedOverRunningConnection |= IsRunning;
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failures-- > 0) throw new HttpRequestException();
            LastToken = deviceKey;
            IsRunning = true;
        }
        public Task DisconnectAsync() { Interlocked.Increment(ref Disconnects); IsRunning = false; return Task.CompletedTask; }
    }
}
