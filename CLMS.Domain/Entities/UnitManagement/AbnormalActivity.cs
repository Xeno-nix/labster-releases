namespace CLMS.Domain.Entities.UnitManagement;

// Only FlaggedApplication is currently detected. The other values reserve names, not detectors.
public enum AbnormalActivityType { FlaggedApplication, HighCpuUsage, HighMemoryUsage, LowDiskSpace, AgentDisconnected }

public sealed record AbnormalActivity(Guid Id, Guid UnitId, AbnormalActivityType Type, Guid? ApplicationId,
    string Details, DateTime DetectedAt, DateTime? EndedAt);
