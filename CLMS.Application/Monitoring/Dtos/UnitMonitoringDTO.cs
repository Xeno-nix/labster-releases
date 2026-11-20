namespace CLMS.Application.DTOs.Monitoring;

public enum UnitMonitoringStatus { Available, InUse, Offline }

public sealed class UnitMonitoringDTO
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public byte Number { get; init; }
    public int LaboratoryId { get; init; }
    public string Laboratory { get; init; } = "";
    public string? SerialNumber { get; init; }
    public string? IpAddress { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public bool IsOnline { get; init; }
    public DateTime? SessionStartedUtc { get; init; }
    public string? CurrentUser { get; init; }
    public CLMS.Shared.Dtos.NetworkResourceReading? NetworkResources { get; init; }
    public DateTime? NetworkResourcesUtc { get; init; }

    public bool IsAvailableOnline(DateTime nowUtc) => IsOnline && LastHeartbeatUtc.HasValue
        && LastHeartbeatUtc.Value >= nowUtc - TimeSpan.FromMinutes(2);

    // Match the existing cleanup worker's two-minute heartbeat threshold.
    public UnitMonitoringStatus GetStatus(DateTime nowUtc)
    {
        if (!IsAvailableOnline(nowUtc))
            return UnitMonitoringStatus.Offline;
        return SessionStartedUtc.HasValue ? UnitMonitoringStatus.InUse : UnitMonitoringStatus.Available;
    }
}
