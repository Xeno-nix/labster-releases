namespace CLMS.Web.Services.AgentConnections;

public sealed record AgentPresence(string ConnectionId, DateTime HeartbeatUtc, string? IpAddress, bool Connected)
{
    public bool HeartbeatConfirmed { get; init; } = true;
}
public sealed record AgentCredentialConnection(string ConnectionId, int Generation, Guid CredentialId);

/// <summary>Live authenticated presence for this web-server process; no schema migration required.</summary>
public sealed class AgentPresenceStore(TimeProvider clock)
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, AgentPresence> units = [];
    private readonly Dictionary<Guid, AgentCredentialConnection> credentials = [];

    public void AssociateCredential(Guid unitId, string connectionId, int generation, Guid credentialId)
    {
        lock (sync)
            if (units.TryGetValue(unitId, out var current) && current.ConnectionId == connectionId && current.Connected)
                credentials[unitId] = new(connectionId, generation, credentialId);
    }

    public AgentCredentialConnection? Credential(Guid unitId, string connectionId)
    {
        lock (sync) return credentials.TryGetValue(unitId, out var credential) && credential.ConnectionId == connectionId
            && units.TryGetValue(unitId, out var current) && current.ConnectionId == connectionId && current.Connected ? credential : null;
    }

    public void Connected(Guid unitId, string connectionId, string? ipAddress)
    {
        lock (sync)
        {
            var lastHeartbeat = units.GetValueOrDefault(unitId)?.HeartbeatUtc ?? DateTime.MinValue;
            units[unitId] = new(connectionId, lastHeartbeat, ipAddress, true) { HeartbeatConfirmed = false };
        }
    }

    public void Heartbeat(Guid unitId, string connectionId)
    {
        lock (sync)
            if (units.TryGetValue(unitId, out var current) && current.ConnectionId == connectionId && current.Connected)
                units[unitId] = current with { HeartbeatUtc = clock.GetUtcNow().UtcDateTime, HeartbeatConfirmed = true };
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
