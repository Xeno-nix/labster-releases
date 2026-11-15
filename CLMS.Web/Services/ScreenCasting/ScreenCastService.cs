using System.Security.Claims;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.ScreenCasting;

public sealed record ScreenCastFrameSnapshot(byte[] JpegBytes, long Sequence);

/// <summary>Single-server live sessions. Retains only the newest bounded frame per viewed unit.</summary>
public sealed class ScreenCastService(IHubContext<AgentConnectionHub> hub, TimeProvider clock,
    ILogger<ScreenCastService> logger) : BackgroundService
{
    public static readonly TimeSpan ViewerLease = TimeSpan.FromSeconds(15);
    private readonly object sync = new();
    private readonly Dictionary<Guid, string> agents = [];
    private readonly Dictionary<Guid, Session> sessions = [];

    private sealed class Session(Guid id, Guid unitId, string connectionId, string viewerId, DateTimeOffset leaseUntil)
    {
        public Guid Id { get; } = id;
        public Guid UnitId { get; } = unitId;
        public string ConnectionId { get; } = connectionId;
        public string ViewerId { get; } = viewerId;
        public DateTimeOffset LeaseUntil { get; set; } = leaseUntil;
        public DateTimeOffset LastFrameAt { get; set; } = DateTimeOffset.MinValue;
        public ScreenCastFrameSnapshot? Frame { get; set; }
    }

    private static string RequireViewer(ClaimsPrincipal viewer)
    {
        var id = viewer.FindFirstValue(ClaimTypes.NameIdentifier);
        if (viewer.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id)
            || !(viewer.IsInRole("Admin") || viewer.IsInRole("Faculty")))
            throw new UnauthorizedAccessException("Only authorized staff can view unit screens.");
        return id;
    }

    public IReadOnlyList<Guid> GetConnectedUnits(ClaimsPrincipal viewer)
    {
        RequireViewer(viewer);
        lock (sync) return agents.Keys.OrderBy(id => id).ToArray();
    }

    public bool IsActive(ClaimsPrincipal viewer, Guid sessionId)
    {
        var viewerId = RequireViewer(viewer);
        lock (sync) return sessions.TryGetValue(sessionId, out var session)
            && session.ViewerId == viewerId && session.LeaseUntil > clock.GetUtcNow();
    }

    public async Task AgentConnectedAsync(Guid unitId, string connectionId)
    {
        Session[] ended;
        lock (sync)
        {
            ended = sessions.Values.Where(s => s.UnitId == unitId).ToArray();
            foreach (var session in ended) sessions.Remove(session.Id);
            agents[unitId] = connectionId;
        }
        foreach (var session in ended) await SendStopAsync(session);
    }

    public Task AgentDisconnectedAsync(Guid unitId, string connectionId)
    {
        lock (sync)
        {
            if (agents.GetValueOrDefault(unitId) == connectionId) agents.Remove(unitId);
            foreach (var session in sessions.Values.Where(s => s.ConnectionId == connectionId).ToArray())
                sessions.Remove(session.Id);
        }
        return Task.CompletedTask;
    }

    public async Task<Guid> StartAsync(ClaimsPrincipal viewer, Guid unitId)
    {
        var viewerId = RequireViewer(viewer);
        Session session;
        lock (sync)
        {
            if (!agents.TryGetValue(unitId, out var connectionId))
                throw new InvalidOperationException("This unit is not connected. Refresh the unit list.");
            if (sessions.Values.Any(s => s.UnitId == unitId))
                throw new InvalidOperationException("This unit already has an active viewing session.");
            if (sessions.Values.Count(s => s.ViewerId == viewerId) >= 16)
                throw new InvalidOperationException("Stop an existing viewing session before starting another.");
            session = new(Guid.NewGuid(), unitId, connectionId, viewerId, clock.GetUtcNow() + ViewerLease);
            sessions.Add(session.Id, session);
        }
        try
        {
            await hub.Clients.Client(session.ConnectionId).SendAsync(ScreenCastProtocol.StartCapture, new ScreenCastCommand(session.Id));
            return session.Id;
        }
        catch
        {
            lock (sync) sessions.Remove(session.Id);
            throw;
        }
    }

    public async Task StopAsync(ClaimsPrincipal viewer, Guid sessionId)
    {
        var viewerId = RequireViewer(viewer);
        Session? session;
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out session) || session.ViewerId != viewerId) return;
            sessions.Remove(sessionId);
        }
        await SendStopAsync(session);
    }

    public bool Publish(Guid unitId, string connectionId, ScreenCastFrame frame)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(frame.SessionId, out var session) || session.UnitId != unitId
                || session.ConnectionId != connectionId || agents.GetValueOrDefault(unitId) != connectionId
                || session.LeaseUntil <= clock.GetUtcNow()) return false;
            if (!JpegFrameValidator.IsValid(frame)) throw new HubException("Invalid or oversized JPEG frame.");
            var now = clock.GetUtcNow();
            if (now - session.LastFrameAt < TimeSpan.FromMilliseconds(450)) return true;
            session.Frame = new(frame.JpegBytes, (session.Frame?.Sequence ?? 0) + 1);
            session.LastFrameAt = now;
            return true;
        }
    }

    // Returns false for unknown, expired, or another user's session; never exposes its frames.
    public bool TryGetFrame(ClaimsPrincipal viewer, Guid sessionId, long after, out ScreenCastFrameSnapshot? frame)
    {
        var viewerId = RequireViewer(viewer);
        lock (sync)
        {
            frame = null;
            if (!sessions.TryGetValue(sessionId, out var session) || session.ViewerId != viewerId
                || session.LeaseUntil <= clock.GetUtcNow()) return false;
            // Only real browser requests renew this lease, not a disconnected Blazor circuit's timer.
            session.LeaseUntil = clock.GetUtcNow() + ViewerLease;
            if (session.Frame is { } latest && latest.Sequence > after) frame = latest;
            return true;
        }
    }

    public void CaptureStopped(Guid unitId, string connectionId, Guid sessionId)
    {
        lock (sync)
            if (sessions.TryGetValue(sessionId, out var session) && session.UnitId == unitId && session.ConnectionId == connectionId)
                sessions.Remove(sessionId);
    }

    public async Task ExpireSessionsAsync()
    {
        Session[] expired;
        lock (sync)
        {
            expired = sessions.Values.Where(s => s.LeaseUntil <= clock.GetUtcNow()).ToArray();
            foreach (var session in expired) sessions.Remove(session.Id);
        }
        foreach (var session in expired) await SendStopAsync(session);
    }

    private async Task SendStopAsync(Session session)
    {
        try { await hub.Clients.Client(session.ConnectionId).SendAsync(ScreenCastProtocol.StopCapture, new ScreenCastCommand(session.Id)); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send stop for screen-cast session {SessionId}.", session.Id); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(stoppingToken)) await ExpireSessionsAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

}
