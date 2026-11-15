namespace CLMS.Web.Services.AgentConnections;

public sealed record AgentPresence(string ConnectionId, DateTime HeartbeatUtc, string? IpAddress, bool Connected);

/// <summary>Live authenticated presence for this web-server process; no schema migration required.</summary>
public sealed class AgentPresenceStore(TimeProvider clock)
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, AgentPresence> units = [];

    public void Connected(Guid unitId, string connectionId, string? ipAddress)
    {
        lock (sync) units[unitId] = new(connectionId, clock.GetUtcNow().UtcDateTime, ipAddress, true);
    }

    public void Heartbeat(Guid unitId, string connectionId)
    {
        lock (sync)
            if (units.TryGetValue(unitId, out var current) && current.ConnectionId == connectionId && current.Connected)
                units[unitId] = current with { HeartbeatUtc = clock.GetUtcNow().UtcDateTime };
    }

    public void Disconnected(Guid unitId, string connectionId)
    {
        lock (sync)
            if (units.TryGetValue(unitId, out var current) && current.ConnectionId == connectionId)
                units[unitId] = current with { Connected = false };
    }

    public IReadOnlyDictionary<Guid, AgentPresence> Snapshot()
    {
        lock (sync) return new Dictionary<Guid, AgentPresence>(units);
    }
}
