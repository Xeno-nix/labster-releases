using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.AgentConnections;

public sealed record CoordinatedApplicationScan(ApplicationScanResponse Response, ApplicationPlatform Platform,
    DateTimeOffset RefreshAvailableAt);

/// <summary>Single-server, bounded by registered units. Shares only in-flight scans; never polls agents.</summary>
public sealed class ApplicationScanCoordinator(IHubContext<AgentConnectionHub> hub, AgentPresenceStore presence,
    TimeProvider clock, ILogger<ApplicationScanCoordinator> logger, IServiceScopeFactory scopes,
    IAbnormalActivityNotifications changes) : IApplicationRegistryNotifications
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private readonly object sync = new();
    private readonly Dictionary<Guid, State> units = [];
    private sealed class State(string connection, ApplicationPlatform platform)
    {
        public string Connection = connection;
        public ApplicationPlatform Platform = platform;
        public DateTimeOffset Next;
        public Guid Request;
        public TaskCompletionSource<ApplicationScanResponse>? Pending;
        public Task<CoordinatedApplicationScan>? Running;
        public CoordinatedApplicationScan? Last;
    }

    public void Register(Guid unit, string connection, ApplicationPlatform platform)
    {
        if (platform is not (ApplicationPlatform.Windows or ApplicationPlatform.MacOS))
            throw new HubException("Unsupported application monitoring platform.");
        lock (sync)
        {
            if (units.TryGetValue(unit, out var state))
            {
                if (state.Connection != connection)
                {
                    state.Pending?.TrySetResult(new(state.Request, clock.GetUtcNow(), ApplicationScanStatus.AgentUnavailable, []));
                    state.Pending = null;
                    state.Running = null;
                    state.Last = null;
                }
                state.Connection = connection;
                state.Platform = platform;
            }
            else units.Add(unit, new(connection, platform));
        }
    }

    public Task<CoordinatedApplicationScan> RequestAsync(Guid unit)
    {
        lock (sync)
        {
            var live = presence.Snapshot().GetValueOrDefault(unit);
            if (!units.TryGetValue(unit, out var state) || live is not { Connected: true, HeartbeatConfirmed: true }
                || live.ConnectionId != state.Connection || live.HeartbeatUtc < clock.GetUtcNow().UtcDateTime.AddMinutes(-2))
                return Task.FromResult(new CoordinatedApplicationScan(new(Guid.NewGuid(), clock.GetUtcNow(), ApplicationScanStatus.AgentUnavailable, []),
                    ApplicationPlatform.Both, clock.GetUtcNow()));
            if (state.Running is not null) return state.Running;
            if (state.Next > clock.GetUtcNow())
                return Task.FromResult(state.Last ?? new(new(state.Request, clock.GetUtcNow(), ApplicationScanStatus.AgentUnavailable, []), state.Platform, state.Next));
            state.Request = Guid.NewGuid();
            var started = clock.GetUtcNow();
            state.Next = started + Cooldown;
            state.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<CoordinatedApplicationScan>(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Running = finished.Task;
            _ = RunAsync(unit, state, state.Connection, state.Request, state.Platform, started, state.Next,
                state.Pending, finished);
            return finished.Task;
        }
    }

    private async Task RunAsync(Guid unit, State state, string connection, Guid request, ApplicationPlatform platform,
        DateTimeOffset started, DateTimeOffset next, TaskCompletionSource<ApplicationScanResponse> completion,
        TaskCompletionSource<CoordinatedApplicationScan> finished)
    {
        var response = new ApplicationScanResponse(request, clock.GetUtcNow(), ApplicationScanStatus.Failed, []);
        try
        {
            _ = SendAsync(unit, connection, request);
            try { response = await completion.Task.WaitAsync(Timeout, clock); }
            catch (TimeoutException)
            {
                completion.TrySetResult(new(request, clock.GetUtcNow(), ApplicationScanStatus.TimedOut, []));
                response = await completion.Task;
            }
            // Legacy/stale agent caches can still display applications, but cannot alter activity state.
            if (response.Status == ApplicationScanStatus.Success && response.RegistryRevision.HasValue)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AbnormalActivityService>().ObserveAsync(
                    new(unit, request, started.UtcDateTime, clock.GetUtcNow().UtcDateTime, platform,
                        response.ApplicationIds.ToArray(), response.IsComplete) { RegistryRevision = response.RegistryRevision },
                    response.Status, deadline.Token);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the application observation for unit {UnitId}.", unit);
            response = new(request, clock.GetUtcNow(), ApplicationScanStatus.Failed, []);
        }
        var result = new CoordinatedApplicationScan(response, platform, next);
        lock (sync)
            if (state.Request == request && state.Connection == connection)
            {
                state.Pending?.TrySetResult(response);
                state.Pending = null;
                state.Running = null;
                state.Last = result;
            }
        finished.TrySetResult(result);
    }

    private async Task SendAsync(Guid unit, string connection, Guid request)
    {
        try { await hub.Clients.Client(connection).SendAsync(ApplicationMonitoringProtocol.Scan, new ApplicationScanRequest(request)); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not request applications for unit {UnitId}.", unit);
            Publish(unit, connection, new(request, clock.GetUtcNow(), ApplicationScanStatus.AgentUnavailable, []));
        }
    }

    public bool Publish(Guid unit, string connection, ApplicationScanResponse response)
    {
        lock (sync)
        {
            if (!units.TryGetValue(unit, out var state) || state.Connection != connection || state.Request != response.RequestId
                || state.Pending is null || presence.Snapshot().GetValueOrDefault(unit) is not { Connected: true } live
                || live.ConnectionId != connection) return false;
            if (response.ApplicationIds is null || response.ApplicationIds.Count > 4096
                || !Enum.IsDefined(response.Status))
                response = new(response.RequestId, clock.GetUtcNow(), ApplicationScanStatus.Failed, []);
            else response = response with { ObservedAt = response.ObservedAt.ToUniversalTime() };
            return state.Pending.TrySetResult(response);
        }
    }

    public void Disconnected(Guid unit, string connection)
    {
        lock (sync)
            if (units.TryGetValue(unit, out var state) && state.Connection == connection)
                state.Pending?.TrySetResult(new(state.Request, clock.GetUtcNow(), ApplicationScanStatus.AgentUnavailable, []));
    }

    public async Task RegistryChangedAsync(CancellationToken ct = default)
    {
        changes.NotifyChanged();
        // A missed notification is recovered by the full refresh on reconnect.
        try { await hub.Clients.All.SendAsync(ApplicationMonitoringProtocol.RegistryChanged, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Registry saved; connected-agent notification failed."); }
    }
}
