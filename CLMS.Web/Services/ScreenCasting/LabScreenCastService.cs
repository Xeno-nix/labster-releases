using System.Security.Claims;
using CLMS.Application.Interfaces.Repositories;

namespace CLMS.Web.Services.ScreenCasting;

/// <summary>Single-server lab session history. Only browser requests renew the lab lease.</summary>
public sealed class LabScreenCastService(IServiceScopeFactory scopes, ScreenCastService screens,
    TimeProvider clock, ILogger<LabScreenCastService> logger) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<int, LabSession> sessions = [];
    private bool recovered;

    private sealed class LabSession(int id, int labId, ClaimsPrincipal viewer, HashSet<Guid> units, DateTimeOffset until)
    {
        public int Id { get; } = id;
        public int LabId { get; } = labId;
        public ClaimsPrincipal Viewer { get; } = viewer;
        public HashSet<Guid> Units { get; } = units;
        public DateTimeOffset Until { get; set; } = until;
        public bool Closing { get; set; }
        public bool Failed { get; set; }
        public Dictionary<Guid, Guid> Captures { get; } = [];
    }

    private static Guid RequireViewer(ClaimsPrincipal viewer)
    {
        if (viewer.Identity?.IsAuthenticated != true || !(viewer.IsInRole("Admin") || viewer.IsInRole("Faculty"))
            || !Guid.TryParse(viewer.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new UnauthorizedAccessException("Only authorized staff can start a lab screen-cast session.");
        return id;
    }

    private LabSession Owned(ClaimsPrincipal viewer, int id)
    {
        var viewerId = RequireViewer(viewer);
        if (!sessions.TryGetValue(id, out var session) || RequireViewer(session.Viewer) != viewerId
            || session.Closing || session.Until <= clock.GetUtcNow())
            throw new InvalidOperationException("This lab screen-cast session has ended. Start a new session.");
        return session;
    }

    public async Task<int> StartAsync(ClaimsPrincipal viewer, int labId)
    {
        var viewerId = RequireViewer(viewer);
        await gate.WaitAsync();
        try
        {
            using var scope = scopes.CreateScope();
            var history = scope.ServiceProvider.GetRequiredService<IScreenCastRepository>();
            // A server restart cannot resume capture or browser leases from the previous process.
            if (!recovered)
            {
                await history.RecoverInterruptedAsync(clock.GetUtcNow().UtcDateTime);
                recovered = true;
            }
            if (sessions.Values.Any(s => !s.Closing && s.Until > clock.GetUtcNow() && s.LabId == labId))
                throw new InvalidOperationException("This laboratory already has an active screen-cast session.");
            var units = (await scope.ServiceProvider.GetRequiredService<IUnitMonitoringRepository>().GetAllAsync())
                .Where(u => u.LaboratoryId == labId).Select(u => u.Id).ToHashSet();
            if (units.Count == 0) throw new InvalidOperationException("Select a laboratory with registered workstations.");
            var id = await history.CreateAsync(viewerId, units, clock.GetUtcNow().UtcDateTime);
            sessions.Add(id, new(id, labId, viewer.Clone(), units, clock.GetUtcNow() + ScreenCastService.ViewerLease));
            return id;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> RenewAsync(ClaimsPrincipal viewer, int id)
    {
        await gate.WaitAsync();
        try
        {
            var session = Owned(viewer, id);
            session.Until = clock.GetUtcNow() + ScreenCastService.ViewerLease;
            return true;
        }
        catch (InvalidOperationException) { return false; }
        finally { gate.Release(); }
    }

    public async Task<Guid> StartViewingAsync(ClaimsPrincipal viewer, int id, Guid unitId)
    {
        await gate.WaitAsync();
        try
        {
            var session = Owned(viewer, id);
            if (!session.Units.Contains(unitId)) throw new UnauthorizedAccessException("This PC is outside the selected laboratory.");
            if (session.Captures.ContainsKey(unitId)) throw new InvalidOperationException("This PC already has a preview in this session.");
            // Recheck membership in case the PC was moved after the session started.
            using var scope = scopes.CreateScope();
            var units = await scope.ServiceProvider.GetRequiredService<IUnitMonitoringRepository>().GetAllAsync();
            if (!units.Any(u => u.Id == unitId && u.LaboratoryId == session.LabId))
                throw new InvalidOperationException("This PC is no longer in the selected laboratory.");
            var capture = await screens.StartAsync(viewer, unitId);
            try
            {
                await scope.ServiceProvider.GetRequiredService<IScreenCastRepository>()
                    .SetConnectionAsync(id, unitId, true, clock.GetUtcNow().UtcDateTime);
                session.Captures.Add(unitId, capture);
                return capture;
            }
            catch { await screens.StopAsync(viewer, capture); throw; }
        }
        finally { gate.Release(); }
    }

    public async Task StopViewingAsync(ClaimsPrincipal viewer, int id, Guid capture)
    {
        var viewerId = RequireViewer(viewer);
        await gate.WaitAsync();
        try
        {
            if (!sessions.TryGetValue(id, out var session) || RequireViewer(session.Viewer) != viewerId) return;
            var unit = session.Captures.FirstOrDefault(p => p.Value == capture);
            if (unit.Equals(default(KeyValuePair<Guid, Guid>))) return;
            await screens.StopAsync(viewer, capture);
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IScreenCastRepository>()
                .SetConnectionAsync(id, unit.Key, false, clock.GetUtcNow().UtcDateTime);
            session.Captures.Remove(unit.Key);
        }
        finally { gate.Release(); }
    }

    public async Task EndAsync(ClaimsPrincipal viewer, int id)
    {
        var viewerId = RequireViewer(viewer);
        await gate.WaitAsync();
        try
        {
            if (!sessions.TryGetValue(id, out var session) || RequireViewer(session.Viewer) != viewerId) return;
            session.Closing = true;
            await CloseAsync(session);
        }
        finally { gate.Release(); }
    }

    private async Task CloseAsync(LabSession session)
    {
        // Stop capture even if the database is unavailable; the next sweep retries history writes.
        foreach (var capture in session.Captures.Values) await screens.StopAsync(session.Viewer, capture);
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IScreenCastRepository>()
            .EndAsync(session.Id, session.Failed, clock.GetUtcNow().UtcDateTime);
        sessions.Remove(session.Id);
    }

    public async Task SweepAsync()
    {
        await gate.WaitAsync();
        try
        {
            foreach (var session in sessions.Values.ToArray())
            {
                try
                {
                    if (session.Closing || session.Until <= clock.GetUtcNow())
                    {
                        if (!session.Closing) { session.Closing = true; session.Failed = true; }
                        await CloseAsync(session);
                        continue;
                    }
                    using var scope = scopes.CreateScope();
                    foreach (var (unit, capture) in session.Captures.ToArray())
                    {
                        if (screens.IsActive(session.Viewer, capture)) continue;
                        await scope.ServiceProvider.GetRequiredService<IScreenCastRepository>()
                            .SetConnectionAsync(session.Id, unit, false, clock.GetUtcNow().UtcDateTime);
                        session.Captures.Remove(unit);
                    }
                }
                catch (Exception ex) { logger.LogError(ex, "Could not update lab screen-cast session {SessionId}.", session.Id); }
            }
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync(); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
