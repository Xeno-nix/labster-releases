using CLMS.Domain.Entities.UnitManagement;

namespace CLMS.Shared.Dtos;

public static class ApplicationMonitoringProtocol
{
    public const string GetRegistry = "GetApplicationRegistry";
    public const string RegistryChanged = "ApplicationRegistryChanged";
    public const string Scan = "ScanApplications";
    public const string Publish = "PublishApplicationScan";
}

public sealed record AgentApplication(Guid Id, string ProcessName, ApplicationPlatform Platform);
public sealed record AgentApplicationRegistry(Guid Revision, IReadOnlyList<AgentApplication> Applications);
public sealed record ApplicationScanRequest(Guid RequestId);
public enum ApplicationScanStatus { Success, RegistryUnavailable, Failed, AgentUnavailable, TimedOut }
public sealed record ApplicationScanResponse(Guid RequestId, DateTimeOffset ObservedAt,
    ApplicationScanStatus Status, IReadOnlyList<Guid> ApplicationIds)
{
    public bool IsComplete { get; init; } = true;
    public Guid? RegistryRevision { get; init; }
}
public sealed record DetectedApplication(Guid Id, string Name, string IconPath)
{
    public bool FlagForReview { get; init; }
}
public sealed record ApplicationMonitoringResult(Guid RequestId, DateTimeOffset ObservedAt,
    ApplicationScanStatus Status, IReadOnlyList<DetectedApplication> Applications, DateTimeOffset RefreshAvailableAt);
