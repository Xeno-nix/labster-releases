using CLMS.Application.DTOs.Monitoring;

namespace CLMS.Web.Models;

public static class UnitMonitoringDisplay
{
    public static string FormatNetworkRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_000_000_000 => $"{bytesPerSecond / 1_000_000_000:0.##} GB/s",
        >= 1_000_000 => $"{bytesPerSecond / 1_000_000:0.##} MB/s",
        >= 1_000 => $"{bytesPerSecond / 1_000:0.##} KB/s",
        _ => $"{bytesPerSecond:0.##} B/s"
    };

    public static string Label(this UnitMonitoringStatus status) => status switch
    {
        UnitMonitoringStatus.Available => "Online",
        UnitMonitoringStatus.InUse => "Online",
        UnitMonitoringStatus.Offline => "Offline",
        _ => "Offline"
    };

    public static string CssClass(this UnitMonitoringStatus status) => status switch
    {
        UnitMonitoringStatus.Available => "bg-success/10 text-success",
        UnitMonitoringStatus.InUse => "bg-success/10 text-success",
        _ => "bg-gray-100 text-gray-600"
    };

    public static string FormatUtc(DateTime? timestamp) =>
        timestamp?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "Not reported";
}
