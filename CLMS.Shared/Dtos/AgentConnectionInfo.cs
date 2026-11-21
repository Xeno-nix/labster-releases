namespace CLMS.Shared.Dtos;

public sealed record AgentConnectionInfo(Guid ComputerUnitId, string ConnectionId, DateTimeOffset ServerTimeUtc);

public static class AgentConnectionProtocol
{
    public const string HubPath = "/hubs/agents";
    public const string HeartbeatMethod = "Heartbeat";
    public const string ReportNetworkResourcesMethod = "ReportNetworkResources";
}
