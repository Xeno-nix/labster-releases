using CLMS.Application.DTOs.Monitoring;
using CLMS.Web.Services.AgentConnections;

namespace CLMS.Web.Models;

public static class UnitMonitoringPresence
{
    public static UnitMonitoringDTO Apply(UnitMonitoringDTO unit, AgentPresence? presence) => presence is null ? unit : new()
    {
        Id = unit.Id,
        Name = unit.Name,
        Number = unit.Number,
        LaboratoryId = unit.LaboratoryId,
        Laboratory = unit.Laboratory,
        SerialNumber = unit.SerialNumber,
        IpAddress = presence.IpAddress ?? unit.IpAddress,
        LastHeartbeatUtc = presence.HeartbeatUtc == DateTime.MinValue ? unit.LastHeartbeatUtc : presence.HeartbeatUtc,
        IsOnline = presence.Connected && presence.HeartbeatConfirmed,
        SessionStartedUtc = unit.SessionStartedUtc,
        CurrentUser = unit.CurrentUser,
        NetworkResources = presence.NetworkResources,
        NetworkResourcesUtc = presence.NetworkResourcesUtc
    };
}
