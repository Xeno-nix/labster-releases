using CLMS.Agent.Models;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Shared Windows/Mac transport. This service keeps credentials in memory only.</summary>
public sealed class AgentConnectionService(ILogger<AgentConnectionService> logger, ScreenCastSender screenCast,
    IAgentAccessTokenProvider tokens, IFacultyStreamViewer? facultyViewer = null,
    WorkstationCommandHandler? commands = null, NotificationReceiver? notifications = null) : IAgentConnectionTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private HubConnection? connection;
    private CancellationTokenSource? lifetime;
    private Task? heartbeatTask;
    private readonly NetworkResourceSampler networkResources = new();

    public AgentConnectionSnapshot Snapshot { get; private set; } = new("Disconnected");
    public bool IsRunning => connection is not null && connection.State != HubConnectionState.Disconnected;
    public event Action<AgentConnectionSnapshot>? StatusChanged;

    public async Task ConnectAsync(string serverAddress, string deviceKey, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(serverAddress.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var server)
            || server.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(server.UserInfo)
            || !string.IsNullOrEmpty(server.Query) || !string.IsNullOrEmpty(server.Fragment))
            throw new ArgumentException("Enter an HTTPS server address, for example https://localhost:4312.");
        deviceKey = deviceKey.Trim();
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("An enrolled agent access token is required.");

        await gate.WaitAsync(cancellationToken);
        try
        {
            await DisconnectCoreAsync();
            Publish(new("Connecting"));
            // Initial token acquisition must honor the connection-attempt deadline and Pause.
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var connectionLifetime = lifetime.Token;
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(server, AgentConnectionProtocol.HubPath.TrimStart('/')), options =>
                {
                    options.AccessTokenProvider = () => tokens.GetAccessTokenAsync(connectionLifetime);
                })
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
                .Build();
            connection = hub;
            hub.On<SystemNotification, DesktopNotificationResult>(SystemNotificationProtocol.Receive, notification =>
                notifications is not null && connection == hub
                    ? notifications.ReceiveAsync(notification, connectionLifetime)
                    : Task.FromResult(new DesktopNotificationResult(notification.Id, DesktopNotificationStatus.Unsupported)));
            hub.On<WorkstationCommand, WorkstationCommandResult>(WorkstationCommandProtocol.Execute, command =>
                commands is not null && connection == hub
                    ? commands.ExecuteAsync(command, connectionLifetime)
                    : Task.FromResult(new WorkstationCommandResult(command.Id, WorkstationCommandStatus.Unsupported)));
            hub.On<ScreenCastCommand>(ScreenCastProtocol.StartCapture,
                command => screenCast.StartAsync(hub, command.SessionId, connectionLifetime));
            hub.On<ScreenCastCommand>(ScreenCastProtocol.StopCapture,
                command => screenCast.StopAsync(command.SessionId));
            hub.On<FacultyStreamOffer>(FacultyStreamProtocol.Offer, async offer =>
            {
                if (facultyViewer is null || connection != hub) return;
                try
                {
                    var answer = await facultyViewer.ShowAsync(offer,
                        () => hub.InvokeAsync(FacultyStreamProtocol.Closed, offer.StreamId, offer.PeerId, connectionLifetime), connectionLifetime);
                    if (!string.IsNullOrEmpty(answer))
                        await hub.InvokeAsync(FacultyStreamProtocol.Answer, new FacultyStreamAnswer(offer.StreamId, offer.PeerId, answer), connectionLifetime);
                    else
                        await hub.InvokeAsync(FacultyStreamProtocol.Closed, offer.StreamId, offer.PeerId, connectionLifetime);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Faculty live stream could not open ({ExceptionType}).", ex.GetType().Name);
                    if (connectionLifetime.IsCancellationRequested) await facultyViewer.StopAsync(offer.StreamId);
                    else await facultyViewer.ShowErrorAsync(offer.StreamId, "signaling_failed");
                    try { await hub.InvokeAsync(FacultyStreamProtocol.Closed, offer.StreamId, offer.PeerId, connectionLifetime); }
                    catch { }
                }
            });
            hub.On<Guid>(FacultyStreamProtocol.Stop, stream => connection == hub
                ? facultyViewer?.StopAsync(stream) ?? Task.CompletedTask : Task.CompletedTask);

            hub.Reconnecting += async _ =>
            {
                if (connection != hub) return;
                await screenCast.StopAsync();
                if (facultyViewer is not null) await facultyViewer.StopAsync();
                if (connection == hub) Publish(new("Reconnecting"));
            };
            hub.Reconnected += async _ =>
            {
                try
                {
                    if (connection == hub && lifetime is not null)
                        await AcknowledgeAsync(hub, lifetime.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Agent reconnected but acknowledgement failed.");
                    if (connection == hub) Publish(new("Waiting for server acknowledgement"));
                }
            };
            hub.Closed += async _ =>
            {
                if (connection != hub) return;
                await screenCast.StopAsync();
                if (facultyViewer is not null) await facultyViewer.StopAsync();
                if (connection == hub) Publish(new("Disconnected — waiting for automatic retry"));
            };

            try
            {
                await hub.StartAsync(cancellationToken);
                await AcknowledgeAsync(hub, cancellationToken);
                heartbeatTask = HeartbeatLoopAsync(hub, lifetime.Token);
            }
            catch (Exception ex)
            {
                await DisconnectCoreAsync();
                Publish(new(AgentConnectionErrors.Describe(ex)));
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await gate.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
            Publish(new("Disconnected"));
        }
        finally { gate.Release(); }
    }

    private async Task AcknowledgeAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        var response = await hub.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod, cancellationToken);
        if (connection == hub)
            Publish(new("Connected", response.ComputerUnitId, response.ServerTimeUtc));
        try
        {
            var reading = networkResources.Sample();
            await hub.InvokeAsync(AgentConnectionProtocol.ReportNetworkResourcesMethod, reading, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Network resource reporting failed."); }
    }

    private async Task HeartbeatLoopAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (hub.State != HubConnectionState.Connected) continue;
                try { await AcknowledgeAsync(hub, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Agent heartbeat acknowledgement failed.");
                    if (connection == hub) Publish(new("Waiting for server acknowledgement"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task DisconnectCoreAsync()
    {
        var oldConnection = connection;
        connection = null;
        lifetime?.Cancel();
        try
        {
            await screenCast.StopAsync();
            if (facultyViewer is not null) await facultyViewer.StopAsync();
            if (oldConnection is not null) await oldConnection.DisposeAsync();
            if (heartbeatTask is not null) await heartbeatTask;
        }
        finally
        {
            lifetime?.Dispose();
            lifetime = null;
            heartbeatTask = null;
            networkResources.Reset();
        }
    }

    private void Publish(AgentConnectionSnapshot snapshot)
    {
        Snapshot = snapshot;
        StatusChanged?.Invoke(snapshot);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
