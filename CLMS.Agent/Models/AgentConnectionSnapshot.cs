namespace CLMS.Agent.Models;

public sealed record AgentConnectionSnapshot(
    string Status,
    Guid? ComputerUnitId = null,
    DateTimeOffset? LastAcknowledgedUtc = null);
