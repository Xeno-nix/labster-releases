using CLMS.Agent.Models;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Shared Windows/Mac transport. This service keeps credentials in memory only.</summary>
public sealed class AgentConnectionService(ILogger<AgentConnectionService> logger, ScreenCastSender screenCast,
    IAgentAccessTokenProvider tokens, IFacultyStreamViewer? facultyViewer = null, ApplicationProcessMonitor? applicationMonitor = null,
    WorkstationCommandHandler? commands = null, NotificationReceiver? notifications = null) : IAgentConnectionTransport, IAsyncDisposable
{
    private readonly ApplicationProcessMonitor applications = applicationMonitor ?? new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private HubConnection? connection;
    private CancellationTokenSource? lifetime;
    private Task? heartbeatTask;
    private readonly NetworkResourceSampler networkResources = new();
    private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(15);
    private readonly TimeSpan heartbeatTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<Uri, Func<Task<string?>>, HubConnection> buildConnection = BuildConnection;

    internal AgentConnectionService(ILogger<AgentConnectionService> logger, ScreenCastSender screenCast,
        IAgentAccessTokenProvider tokens, Func<Uri, Func<Task<string?>>, HubConnection> buildConnection,
        TimeSpan heartbeatInterval, TimeSpan heartbeatTimeout, ApplicationProcessMonitor? applicationMonitor = null)
        : this(logger, screenCast, tokens, applicationMonitor)
    {
        this.buildConnection = buildConnection;
        this.heartbeatInterval = heartbeatInterval;
        this.heartbeatTimeout = heartbeatTimeout;
    }

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
            applications.Reset();
            Publish(new("Connecting"));
            var connectionSource = new CancellationTokenSource();
            lifetime = connectionSource;
            var connectionLifetime = connectionSource.Token;
            var hub = buildConnection(new Uri(server, AgentConnectionProtocol.HubPath.TrimStart('/')),
                () => GetTokenAsync(connectionLifetime));
            connection = hub;
            hub.On<SystemNotification, DesktopNotificationResult>(SystemNotificationProtocol.Receive, notification =>
                notifications is not null && connection == hub
                    ? notifications.ReceiveAsync(notification, connectionLifetime)
                    : Task.FromResult(new DesktopNotificationResult(notification.Id, DesktopNotificationStatus.Unsupported)));
            hub.On<WorkstationCommand, WorkstationCommandResult>(WorkstationCommandProtocol.Execute, command =>
                commands is not null && connection == hub
                    ? commands.ExecuteAsync(command, connectionLifetime)
                    : Task.FromResult(new WorkstationCommandResult(command.Id, WorkstationCommandStatus.Unsupported)));
            hub.On(ApplicationMonitoringProtocol.RegistryChanged, () => SynchronizeApplicationsAsync(hub, connectionLifetime));
            hub.On<ApplicationScanRequest>(ApplicationMonitoringProtocol.Scan, async request =>
            {
                var result = await Task.Run(() => applications.Scan(request.RequestId), connectionLifetime);
                await hub.InvokeAsync<bool>(ApplicationMonitoringProtocol.Publish, result, connectionLifetime);
            });
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
                    if (connection == hub)
                    {
                        await AcknowledgeAsync(hub, connectionLifetime);
                        await SynchronizeApplicationsAsync(hub, connectionLifetime);
                    }
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
                // The startup deadline must not own the lifetime of a healthy connection.
                using (cancellationToken.Register(connectionSource.Cancel))
                {
                    await hub.StartAsync(connectionLifetime);
                    await AcknowledgeAsync(hub, connectionLifetime);
                    await SynchronizeApplicationsAsync(hub, connectionLifetime);
                }
                connectionLifetime.ThrowIfCancellationRequested();
                heartbeatTask = HeartbeatLoopAsync(hub, connectionLifetime);
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

    private static HubConnection BuildConnection(Uri uri, Func<Task<string?>> accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(uri, options => options.AccessTokenProvider = accessToken)
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
            .Build();

    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return await tokens.GetAccessTokenAsync(deadline.Token);
    }

    private async Task AcknowledgeAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(heartbeatTimeout);
        var response = await hub.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod, deadline.Token);
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

    private async Task SynchronizeApplicationsAsync(HubConnection hub, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var loaded = await applications.SynchronizeRegistryAsync(token => hub.InvokeAsync<AgentApplicationRegistry>(
                ApplicationMonitoringProtocol.GetRegistry, applications.Platform, token), deadline.Token);
            if (!loaded) logger.LogWarning("Application registry synchronization failed; retaining the previous cache.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Application registry synchronization timed out; retaining the previous cache.");
        }
    }

    private async Task HeartbeatLoopAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(heartbeatInterval);
            var failures = 0;
            string? previousConnection = null;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var connectionId = hub.ConnectionId;
                if (hub.State != HubConnectionState.Connected || connectionId != previousConnection)
                    failures = 0;
                if (connection != hub || hub.State != HubConnectionState.Connected) continue;
                previousConnection = connectionId;
                try
                {
                    await AcknowledgeAsync(hub, cancellationToken);
                    failures = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (connection != hub || hub.State != HubConnectionState.Connected || hub.ConnectionId != connectionId)
                        continue;

                    logger.LogWarning(ex, "Agent heartbeat acknowledgement failed.");
                    Publish(new("Waiting for server acknowledgement"));
                    if (++failures < 2) continue;

                    // The transport can remain connected while application heartbeats are unresponsive.
                    // Close it so the runtime can open a fresh connection using current credentials.
                    try { await hub.StopAsync(cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (Exception stopError)
                    {
                        logger.LogWarning(stopError, "Could not stop the unresponsive agent connection.");
                    }
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
