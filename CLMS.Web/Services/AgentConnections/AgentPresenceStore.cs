using CLMS.Shared.Dtos;

namespace CLMS.Web.Services.AgentConnections;

public sealed record AgentPresence(string ConnectionId, DateTime HeartbeatUtc, string? IpAddress, bool Connected)
{
    public NetworkResourceReading? NetworkResources { get; init; }
    public DateTime? NetworkResourcesUtc { get; init; }
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

    public void ReportNetworkResources(Guid unitId, string connectionId, NetworkResourceReading? reading)
    {
        if (reading is { IsValid: false }) return;
        lock (sync)
            if (units.TryGetValue(unitId, out var current) && current.ConnectionId == connectionId && current.Connected)
                units[unitId] = current with
                {
                    NetworkResources = reading,
                    NetworkResourcesUtc = reading is null ? null : clock.GetUtcNow().UtcDateTime
                };
    }

    public IReadOnlyDictionary<Guid, AgentPresence> Snapshot()
    {
        lock (sync) return new Dictionary<Guid, AgentPresence>(units);
    }
}
