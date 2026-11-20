using CLMS.Agent.Models;
using CLMS.Agent.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class ScreenCastSenderLifecycleTests
{
    [Fact]
    public async Task StopWaitsForCancellationAndReleasesNativeCapture()
    {
        var capture = new Capture(block: true);
        var sender = new ScreenCastSender(capture, NullLogger<ScreenCastSender>.Instance);
        await using var hub = new HubConnectionBuilder().WithUrl("https://localhost").Build();
        await sender.StartAsync(hub, Guid.NewGuid(), CancellationToken.None);
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.StopAsync();
        Assert.True(capture.Cancelled);
        Assert.Equal(1, capture.StopCount);
        Assert.False(sender.IsSharing);
    }

    [Fact]
    public async Task CaptureFailureAlsoReleasesNativeCapture()
    {
        var capture = new Capture(block: false);
        var sender = new ScreenCastSender(capture, NullLogger<ScreenCastSender>.Instance);
        await using var hub = new HubConnectionBuilder().WithUrl("https://localhost").Build();
        await sender.StartAsync(hub, Guid.NewGuid(), CancellationToken.None);
        await capture.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.StopAsync();
        Assert.Equal(1, capture.StopCount);
        Assert.False(sender.IsSharing);
    }

    private sealed class Capture(bool block) : IScreenCaptureService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public int StopCount { get; private set; }

        public async Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (block)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            throw new InvalidOperationException("Display unavailable");
        }

        public Task StopAsync()
        {
            StopCount++;
            Stopped.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
