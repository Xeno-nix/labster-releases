using System.Threading.Channels;
using CLMS.Agent.Models;
using CLMS.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentRuntimeTests
{
    private static AgentRuntimeService Create(Setup setup, Transport transport) =>
        new(setup, transport, NullLogger<AgentRuntimeService>.Instance, TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task MissingSetupDoesNotAttemptAConnection()
    {
        var transport = new Transport();
        await using var runtime = Create(new Setup { Key = null }, transport);
        Assert.False(await runtime.StartAsync());
        Assert.Equal(0, transport.Attempts);
        Assert.Contains("Setup required", runtime.Status);
    }

    [Fact]
    public async Task UnreadableSecureStorageKeepsSetupAvailable()
    {
        var transport = new Transport();
        await using var runtime = Create(new Setup { FailReading = true }, transport);
        Assert.False(await runtime.StartAsync());
        Assert.Equal(0, transport.Attempts);
        Assert.Contains("Cannot read saved setup", runtime.Status);
    }

    [Fact]
    public async Task InitialFailureIsRetriedWithoutOpeningSettings()
    {
        var transport = new Transport { FailuresRemaining = 1 };
        await using var runtime = Create(new Setup(), transport);
        Assert.True(await runtime.StartAsync());
        Assert.Equal(1, await transport.NextAttemptAsync());
        Assert.Equal(2, await transport.NextAttemptAsync());
        Assert.True(transport.IsRunning);
    }

    [Fact]
    public async Task ClosedConnectionIsStartedAgain()
    {
        var transport = new Transport();
        await using var runtime = Create(new Setup(), transport);
        await runtime.StartAsync();
        await transport.NextAttemptAsync();
        transport.IsRunning = false; // SignalR's built-in reconnect attempts were exhausted.
        Assert.Equal(2, await transport.NextAttemptAsync());
        Assert.True(transport.IsRunning);
    }

    [Fact]
    public async Task PauseCancelsAnInFlightConnectionAndLeavesNoRetryLoop()
    {
        var transport = new Transport { BlockFirstAttempt = true };
        await using var runtime = Create(new Setup(), transport);
        await runtime.StartAsync();
        await transport.NextAttemptAsync();
        await runtime.StopAsync();
        Assert.True(transport.AttemptCancelled);
        Assert.False(transport.IsRunning);
        Assert.Equal(1, transport.Attempts);
        Assert.Contains("paused", runtime.Status);
    }

    [Fact]
    public async Task RestartCancelsOldAttemptAndLoadsNewSavedCredentials()
    {
        var setup = new Setup();
        var transport = new Transport { BlockFirstAttempt = true };
        await using var runtime = Create(setup, transport);
        await runtime.StartAsync();
        await transport.NextAttemptAsync();
        setup.Key = "replacement-device-key-at-least-32-characters";
        setup.ServerAddress = "https://replacement.example";
        await runtime.StartAsync();
        Assert.Equal(2, await transport.NextAttemptAsync());
        Assert.True(transport.AttemptCancelled);
        Assert.Equal(setup.Key, transport.LastKey);
        Assert.Equal(setup.ServerAddress, transport.LastServer);
    }

    [Fact]
    public async Task RestartDisconnectsThePreviousConnectionBeforeConnecting()
    {
        var transport = new Transport();
        await using var runtime = Create(new Setup(), transport);
        await runtime.StartAsync();
        await transport.NextAttemptAsync();
        await runtime.StartAsync();
        await transport.NextAttemptAsync();
        Assert.False(transport.OverlappingConnections);
        Assert.True(transport.IsRunning);
    }

    [Fact]
    public async Task DisposeDisconnectsAndStopsBackgroundWork()
    {
        var transport = new Transport();
        var runtime = Create(new Setup(), transport);
        await runtime.StartAsync();
        await transport.NextAttemptAsync();
        await runtime.DisposeAsync();
        Assert.False(transport.IsRunning);
        Assert.Equal(1, transport.Attempts);
    }

    private sealed class Setup : IAgentSetupStore
    {
        public string ServerAddress { get; set; } = "https://clms.example";
        public string? Key { get; set; } = "test-device-key-at-least-32-characters";
        public bool FailReading { get; init; }
        public Task<string?> GetDeviceKeyAsync() => FailReading
            ? Task.FromException<string?>(new InvalidOperationException("Storage unavailable")) : Task.FromResult(Key);
    }

    private sealed class Transport : IAgentConnectionTransport
    {
        private readonly Channel<int> attempts = Channel.CreateUnbounded<int>();
        private int attemptCount;
        private volatile bool isRunning;
        public int Attempts => attemptCount;
        public int FailuresRemaining { get; set; }
        public bool BlockFirstAttempt { get; init; }
        public bool AttemptCancelled { get; private set; }
        public bool OverlappingConnections { get; private set; }
        public string? LastKey { get; private set; }
        public string? LastServer { get; private set; }
        public bool IsRunning { get => isRunning; set => isRunning = value; }
        public AgentConnectionSnapshot Snapshot { get; } = new("Test");

        public Task<int> NextAttemptAsync() => attempts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public async Task ConnectAsync(string serverAddress, string deviceKey, CancellationToken cancellationToken = default)
        {
            OverlappingConnections |= IsRunning;
            LastKey = deviceKey;
            LastServer = serverAddress;
            var attempt = Interlocked.Increment(ref attemptCount);
            if (BlockFirstAttempt && attempt == 1)
            {
                attempts.Writer.TryWrite(attempt);
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { AttemptCancelled = true; throw; }
            }
            if (FailuresRemaining-- > 0)
            {
                attempts.Writer.TryWrite(attempt);
                throw new HttpRequestException("Server unavailable");
            }
            IsRunning = true;
            attempts.Writer.TryWrite(attempt);
        }

        public Task DisconnectAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
    }
}
