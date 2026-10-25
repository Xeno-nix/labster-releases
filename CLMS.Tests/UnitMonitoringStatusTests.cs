using CLMS.Application.DTOs.Monitoring;
using Xunit;

namespace CLMS.Tests;

public class UnitMonitoringStatusTests
{
    [Theory]
    [InlineData(null, false, false, UnitMonitoringStatus.NoSignal)]
    [InlineData(null, true, true, UnitMonitoringStatus.NoSignal)]
    [InlineData(30, true, false, UnitMonitoringStatus.Idle)]
    [InlineData(30, true, true, UnitMonitoringStatus.InUse)]
    [InlineData(30, false, true, UnitMonitoringStatus.Offline)]
    [InlineData(120, true, true, UnitMonitoringStatus.InUse)]
    [InlineData(121, true, true, UnitMonitoringStatus.Offline)]
    [InlineData(3600, true, false, UnitMonitoringStatus.Offline)]
    public void Status_RequiresFreshOnlineHeartbeatBeforeReportingUsage(
        int? heartbeatAgeSeconds, bool online, bool openSession, UnitMonitoringStatus expected)
    {
        var now = new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc);
        var unit = new UnitMonitoringDTO
        {
            IsOnline = online,
            LastHeartbeatUtc = heartbeatAgeSeconds.HasValue ? now.AddSeconds(-heartbeatAgeSeconds.Value) : null,
            SessionStartedUtc = openSession ? now.AddHours(-1) : null
        };

        Assert.Equal(expected, unit.GetStatus(now));
    }
}
