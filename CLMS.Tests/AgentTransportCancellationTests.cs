using CLMS.Agent.Models;
using CLMS.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentTransportCancellationTests
{
    [Fact]
    public async Task CancellingConnectAlsoCancelsItsPendingTokenProvider()
    {
        var tokens = new WaitingTokens();
        var sender = new ScreenCastSender(new UnusedCapture(), NullLogger<ScreenCastSender>.Instance);
        await using var transport = new AgentConnectionService(NullLogger<AgentConnectionService>.Instance, sender, tokens);
        using var cancellation = new CancellationTokenSource();
        // The provider blocks before any request is sent; no running server is required.
        var connecting = transport.ConnectAsync("https://localhost:1", "initial-jwt", cancellation.Token);
        await tokens.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(transport.IsRunning);
        Assert.False(sender.IsSharing);
    }

    private sealed class WaitingTokens : IAgentAccessTokenProvider
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
    private sealed class UnusedCapture : IScreenCaptureService
    {
        public Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Capture must not start while connecting.");
    }
}
