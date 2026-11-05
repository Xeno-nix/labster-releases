using CLMS.Application.DTOs.Monitoring;
using Xunit;

namespace CLMS.Tests;

public class UnitMonitoringStatusTests
{
    [Theory]
    [InlineData(null, true, false)]
    [InlineData(30, false, false)]
    [InlineData(30, true, true)]
    [InlineData(120, true, true)]
    [InlineData(121, true, false)]
    public void Availability_RequiresOnlineFlagAndRecentHeartbeat(int? ageSeconds, bool online, bool expected)
    {
        var now = new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
        var unit = new UnitMonitoringDTO
        {
            IsOnline = online,
            LastHeartbeatUtc = ageSeconds.HasValue ? now.AddSeconds(-ageSeconds.Value) : null
        };
        Assert.Equal(expected, unit.IsAvailableOnline(now));
    }

    [Theory]
    [InlineData(null, false, false, UnitMonitoringStatus.Offline)]
    [InlineData(null, true, true, UnitMonitoringStatus.Offline)]
    [InlineData(30, true, false, UnitMonitoringStatus.Available)]
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
