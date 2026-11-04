using CLMS.Application.DTOs.Monitoring;

namespace CLMS.Web.Models;

public static class UnitAvailabilityPreview
{
    public static UnitMonitoringDTO Apply(UnitMonitoringDTO unit, UnitMonitoringStatus status, DateTime nowUtc) => new()
    {
        Id = unit.Id,
        Name = unit.Name,
        Number = unit.Number,
        LaboratoryId = unit.LaboratoryId,
        Laboratory = unit.Laboratory,
        SerialNumber = unit.SerialNumber,
        IpAddress = unit.IpAddress,
        IsOnline = status != UnitMonitoringStatus.Offline,
        LastHeartbeatUtc = nowUtc,
        SessionStartedUtc = status == UnitMonitoringStatus.InUse ? nowUtc : null,
        CurrentUser = status == UnitMonitoringStatus.InUse ? "Demo user" : null
    };
}
