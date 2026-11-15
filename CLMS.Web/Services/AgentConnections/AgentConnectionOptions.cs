namespace CLMS.Web.Services.AgentConnections;

/// <summary>Manually provisioned device identities until the agent registry supplies credentials.</summary>
public sealed class AgentConnectionOptions
{
    public const string SectionName = "AgentConnections";
    public List<AgentDeviceCredential> Devices { get; set; } = [];
}

public sealed class AgentDeviceCredential
{
    public Guid ComputerUnitId { get; set; }
    public string KeySha256 { get; set; } = string.Empty;
}
