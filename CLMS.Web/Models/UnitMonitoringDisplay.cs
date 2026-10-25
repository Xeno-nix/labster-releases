using CLMS.Application.DTOs.Monitoring;

namespace CLMS.Web.Models;

public static class UnitMonitoringDisplay
{
    public static string Label(this UnitMonitoringStatus status) => status switch
    {
        UnitMonitoringStatus.Idle => "Idle",
        UnitMonitoringStatus.InUse => "In use",
        UnitMonitoringStatus.Offline => "Offline",
        _ => "No signal"
    };

    public static string CssClass(this UnitMonitoringStatus status) => status switch
    {
        UnitMonitoringStatus.Idle => "bg-success/10 text-success",
        UnitMonitoringStatus.InUse => "bg-info/10 text-info",
        _ => "bg-gray-100 text-gray-600"
    };

    public static string FormatUtc(DateTime? timestamp) =>
        timestamp?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Not reported";
}
