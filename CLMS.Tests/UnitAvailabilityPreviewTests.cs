using CLMS.Application.DTOs.Monitoring;
using CLMS.Web.Models;
using Xunit;

namespace CLMS.Tests;

public class UnitAvailabilityPreviewTests
{
    [Theory]
    [InlineData(UnitMonitoringStatus.Available)]
    [InlineData(UnitMonitoringStatus.InUse)]
    [InlineData(UnitMonitoringStatus.Offline)]
    public void Preview_ChangesStatusWithoutMutatingLiveUnit(UnitMonitoringStatus status)
    {
        var now = DateTime.UtcNow;
        var live = new UnitMonitoringDTO { Id = Guid.NewGuid(), Name = "PC 01", LaboratoryId = 1 };

        var preview = UnitAvailabilityPreview.Apply(live, status, now);

        Assert.Equal(status, preview.GetStatus(now));
        Assert.Equal(live.Id, preview.Id);
        Assert.Equal(live.Name, preview.Name);
        Assert.Equal(live.LaboratoryId, preview.LaboratoryId);
        Assert.Equal(UnitMonitoringStatus.Offline, live.GetStatus(now));
        Assert.Null(live.LastHeartbeatUtc);
        Assert.Null(live.SessionStartedUtc);
    }
}
