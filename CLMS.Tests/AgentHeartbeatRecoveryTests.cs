using CLMS.Agent.Models;
using CLMS.Agent.Services;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentHeartbeatRecoveryTests
{
    [Fact]
    public async Task UnacknowledgedHeartbeatsCloseAStuckTransportForTheRuntimeToRetry()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<HeartbeatState>();
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        app.MapHub<TestHeartbeatHub>(AgentConnectionProtocol.HubPath);
        await app.StartAsync();

        var state = app.Services.GetRequiredService<HeartbeatState>();
        var sender = new ScreenCastSender(new UnusedCapture(), NullLogger<ScreenCastSender>.Instance);
        await using var service = new AgentConnectionService(NullLogger<AgentConnectionService>.Instance, sender,
            new Tokens(), (uri, accessToken) => new HubConnectionBuilder().WithUrl(uri, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
                options.AccessTokenProvider = accessToken;
            }).Build(), TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(2));

        await service.ConnectAsync("https://localhost", "initial-token");
        Assert.True(service.IsRunning);
        Assert.Equal("Connected", service.Snapshot.Status);

        state.Hang = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (service.IsRunning) await Task.Delay(20, deadline.Token);
        Assert.False(sender.IsSharing);

        state.Hang = false;
        await service.ConnectAsync("https://localhost", "replacement-token");
        Assert.True(service.IsRunning);
        Assert.Equal("Connected", service.Snapshot.Status);
    }

    public sealed class HeartbeatState
    {
        public volatile bool Hang;
    }

    public sealed class TestHeartbeatHub(HeartbeatState state) : Hub
    {
        public async Task<AgentConnectionInfo> Heartbeat()
        {
            if (state.Hang) await Task.Delay(Timeout.Infinite, Context.ConnectionAborted);
            return new(Guid.Empty, Context.ConnectionId, DateTimeOffset.UtcNow);
        }
    }

    private sealed class Tokens : IAgentAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("test-token");
    }

    private sealed class UnusedCapture : IScreenCaptureService
    {
        public Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
