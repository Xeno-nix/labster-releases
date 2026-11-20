using System.Security.Claims;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.UnitRegistry;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.ScreenCasting;

/// <summary>Faculty-to-lab WebRTC signaling within an active monitoring session.</summary>
public sealed class FacultyStreamService : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly AgentPresenceStore presence;
    private readonly IHubContext<AgentConnectionHub> hub;
    private readonly TimeProvider clock;
    private readonly ILogger<FacultyStreamService> logger;
    private readonly LabScreenCastService monitoring;

    public FacultyStreamService(IServiceScopeFactory scopes, AgentPresenceStore presence,
        IHubContext<AgentConnectionHub> hub, TimeProvider clock, ILogger<FacultyStreamService> logger,
        LabScreenCastService monitoring)
    {
        this.scopes = scopes;
        this.presence = presence;
        this.hub = hub;
        this.clock = clock;
        this.logger = logger;
        this.monitoring = monitoring;
        monitoring.SessionEnding += StopSessionAsync;
    }
    public static readonly TimeSpan BrowserLease = TimeSpan.FromSeconds(90);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, Stream> streams = [];

    private sealed class Stream(Guid id, string owner, int lab, string laboratory, string presenter,
        DateTimeOffset until, int monitoringSessionId, CancellationToken sessionLifetime)
    {
        public Guid Id { get; } = id;
        public string Owner { get; } = owner;
        public int Lab { get; } = lab;
        public string Laboratory { get; } = laboratory;
        public string Presenter { get; } = presenter;
        public DateTimeOffset Until { get; set; } = until;
        public int MonitoringSessionId { get; } = monitoringSessionId;
        public CancellationToken SessionLifetime { get; } = sessionLifetime;
        public Dictionary<Guid, Peer> Peers { get; } = [];
        public HashSet<Guid> Declined { get; } = [];
    }
    private sealed class Peer(Guid unit, string name, string connection)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Unit { get; } = unit;
        public string Name { get; } = name;
        public string Connection { get; } = connection;
        public bool Offered { get; set; }
        public string? Answer { get; set; }
    }

    private static string RequireStaff(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id)
            || !(user.IsInRole("Faculty") || user.IsInRole("Admin")))
            throw new UnauthorizedAccessException("Only faculty and administrators can broadcast a screen.");
        return id;
    }
    private Stream Owned(ClaimsPrincipal user, Guid id)
    {
        var owner = RequireStaff(user);
        if (!streams.TryGetValue(id, out var stream) || stream.Owner != owner || stream.Until <= clock.GetUtcNow()
            || stream.SessionLifetime.IsCancellationRequested)
            throw new InvalidOperationException("This live stream has ended. Start a new live stream.");
        return stream;
    }

    public Task<FacultyStreamState> StartAsync(ClaimsPrincipal user, int lab, int monitoringSessionId)
    {
        RequireStaff(user);
        return monitoring.StartBroadcastAsync(user, monitoringSessionId, lab,
            token => StartCoreAsync(user, lab, monitoringSessionId, token));
    }

    private async Task<FacultyStreamState> StartCoreAsync(ClaimsPrincipal user, int lab, int monitoringSessionId,
        CancellationToken sessionLifetime)
    {
        var owner = RequireStaff(user);
        await gate.WaitAsync();
        try
        {
            await SweepCoreAsync();
            if (streams.Values.Any(s => s.Lab == lab))
                throw new InvalidOperationException("This laboratory already has a faculty live stream.");
            if (streams.Values.Any(s => s.Owner == owner))
                throw new InvalidOperationException("Stop your existing live stream before starting another.");
            using var scope = scopes.CreateScope();
            var laboratory = await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>().GetByIdAsync(lab)
                ?? throw new InvalidOperationException("Select an existing laboratory.");
            var presenter = user.FindFirstValue(ClaimTypes.Name) ?? "Faculty";
            if (presenter.Length > 100) presenter = presenter[..100];
            var stream = new Stream(Guid.NewGuid(), owner, lab, laboratory.Name, presenter,
                clock.GetUtcNow() + BrowserLease, monitoringSessionId, sessionLifetime);
            streams.Add(stream.Id, stream);
            return Snapshot(stream);
        }
        finally { gate.Release(); }
    }

    // Called by the broadcaster's browser, including when presenting another application.
    public async Task<FacultyStreamState> PollAsync(ClaimsPrincipal user, Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var stream = Owned(user, id);
            using var scope = scopes.CreateScope();
            var units = (await scope.ServiceProvider.GetRequiredService<IUnitMonitoringRepository>().GetAllAsync())
                .Where(u => u.LaboratoryId == stream.Lab).ToDictionary(u => u.Id);
            var connected = presence.Snapshot();
            var eligible = new Dictionary<Guid, string>();
            var registry = scope.ServiceProvider.GetRequiredService<IUnitRegistryRepository>();
            foreach (var unit in units.Values)
            {
                if (!connected.TryGetValue(unit.Id, out var current) || !current.Connected) continue;
                var credential = presence.Credential(unit.Id, current.ConnectionId);
                if (credential is not null && await registry.IsAuthorizedAsync(unit.Id, credential.Generation,
                    credential.CredentialId, CancellationToken.None)) eligible[unit.Id] = current.ConnectionId;
            }
            foreach (var peer in stream.Peers.Values.ToArray())
                if (!eligible.TryGetValue(peer.Unit, out var connection) || connection != peer.Connection)
                {
                    stream.Peers.Remove(peer.Id);
                    await StopPeerAsync(stream, peer);
                }
            foreach (var unit in units.Values)
                if (!stream.Declined.Contains(unit.Id) && !stream.Peers.Values.Any(p => p.Unit == unit.Id)
                    && eligible.TryGetValue(unit.Id, out var connection))
                {
                    var peer = new Peer(unit.Id, unit.Name, connection);
                    stream.Peers.Add(peer.Id, peer);
                }
            stream.Until = clock.GetUtcNow() + BrowserLease;
            return Snapshot(stream);
        }
        finally { gate.Release(); }
    }

    public async Task OfferAsync(ClaimsPrincipal user, Guid id, Guid peerId, string sdp)
    {
        ValidateSdp(sdp);
        await gate.WaitAsync();
        try
        {
            var stream = Owned(user, id);
            if (!stream.Peers.TryGetValue(peerId, out var peer) || peer.Offered)
                throw new InvalidOperationException("This workstation connection has changed.");
            using var scope = scopes.CreateScope();
            var units = await scope.ServiceProvider.GetRequiredService<IUnitMonitoringRepository>().GetAllAsync();
            var credential = presence.Credential(peer.Unit, peer.Connection);
            if (!units.Any(u => u.Id == peer.Unit && u.LaboratoryId == stream.Lab) || credential is null
                || !await scope.ServiceProvider.GetRequiredService<IUnitRegistryRepository>()
                    .IsAuthorizedAsync(peer.Unit, credential.Generation, credential.CredentialId, CancellationToken.None))
                throw new InvalidOperationException("This workstation is no longer available in this laboratory.");
            peer.Offered = true;
            try
            {
                await hub.Clients.Client(peer.Connection).SendAsync(FacultyStreamProtocol.Offer,
                    new FacultyStreamOffer(id, peerId, stream.Presenter, sdp));
            }
            catch { peer.Offered = false; throw; }
        }
        finally { gate.Release(); }
    }

    public async Task AnswerAsync(Guid unit, string connection, FacultyStreamAnswer answer)
    {
        ValidateSdp(answer.Sdp);
        await gate.WaitAsync();
        try
        {
            if (!streams.TryGetValue(answer.StreamId, out var stream) || stream.Until <= clock.GetUtcNow()
                || stream.SessionLifetime.IsCancellationRequested
                || !stream.Peers.TryGetValue(answer.PeerId, out var peer) || !peer.Offered
                || peer.Unit != unit || peer.Connection != connection || peer.Answer is not null)
                throw new HubException("The live stream connection is no longer available.");
            using var scope = scopes.CreateScope();
            var units = await scope.ServiceProvider.GetRequiredService<IUnitMonitoringRepository>().GetAllAsync();
            if (!units.Any(u => u.Id == unit && u.LaboratoryId == stream.Lab))
                throw new HubException("The workstation is no longer in this laboratory.");
            peer.Answer = answer.Sdp;
        }
        finally { gate.Release(); }
    }

    public async Task ClosedAsync(Guid unit, string connection, Guid id, Guid peerId)
    {
        await gate.WaitAsync();
        try
        {
            if (streams.TryGetValue(id, out var stream) && stream.Peers.TryGetValue(peerId, out var peer)
                && peer.Unit == unit && peer.Connection == connection)
            {
                stream.Peers.Remove(peerId);
                stream.Declined.Add(unit); // Closing a viewer must not reopen it on the next poll.
            }
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(ClaimsPrincipal user, Guid id)
    {
        var owner = RequireStaff(user);
        await gate.WaitAsync();
        try
        {
            if (streams.TryGetValue(id, out var stream) && stream.Owner == owner)
                await EndAsync(stream);
        }
        finally { gate.Release(); }
    }
    private static FacultyStreamState Snapshot(Stream stream) => new(stream.Id, stream.Laboratory,
        stream.Peers.Values.Select(p => new FacultyStreamPeer(p.Id, p.Name, p.Answer)).ToArray());
    private static void ValidateSdp(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > FacultyStreamProtocol.MaximumSdpLength || !sdp.StartsWith("v=0", StringComparison.Ordinal))
            throw new ArgumentException("Invalid live stream connection description.");
    }
    private async Task StopPeerAsync(Stream stream, Peer peer)
    {
        if (!peer.Offered) return;
        try { await hub.Clients.Client(peer.Connection).SendAsync(FacultyStreamProtocol.Stop, stream.Id); }
        catch { logger.LogWarning("Could not notify a workstation that a faculty stream ended."); }
    }
    private async Task EndAsync(Stream stream)
    {
        streams.Remove(stream.Id);
        foreach (var peer in stream.Peers.Values) await StopPeerAsync(stream, peer);
    }
    private async Task SweepCoreAsync()
    {
        foreach (var stream in streams.Values.Where(s => s.Until <= clock.GetUtcNow()
            || s.SessionLifetime.IsCancellationRequested).ToArray()) await EndAsync(stream);
    }
    private async Task StopSessionAsync(int sessionId)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var stream in streams.Values.Where(s => s.MonitoringSessionId == sessionId).ToArray())
                await EndAsync(stream);
        }
        finally { gate.Release(); }
    }

    public override void Dispose()
    {
        monitoring.SessionEnding -= StopSessionAsync;
        base.Dispose();
    }
    public async Task SweepAsync()
    {
        await gate.WaitAsync();
        try { await SweepCoreAsync(); }
        finally { gate.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
