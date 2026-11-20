using CLMS.Application.DTOs.Monitoring;
using CLMS.Web.Models;
using CLMS.Web.Services.AgentConnections;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentPresenceTests
{
    private readonly Guid unitId = Guid.NewGuid();

    [Fact]
    public void FreshAgentOverridesOfflineDatabaseAndKeepsUnitAndSessionDetails()
    {
        var clock = new Clock();
        var store = new AgentPresenceStore(clock);
        store.Connected(unitId, "connection", "192.0.2.1");
        Assert.Equal(UnitMonitoringStatus.Offline,
            UnitMonitoringPresence.Apply(new(), store.Snapshot()[unitId]).GetStatus(clock.Now.UtcDateTime));
        store.Heartbeat(unitId, "connection");
        var unit = UnitMonitoringPresence.Apply(new UnitMonitoringDTO
        {
            Id = unitId, Name = "PC-01", LaboratoryId = 2, Laboratory = "Lab 2", Number = 1,
            CurrentUser = "Student", SessionStartedUtc = clock.Now.UtcDateTime.AddMinutes(-5)
        }, store.Snapshot()[unitId]);
        Assert.Equal(UnitMonitoringStatus.InUse, unit.GetStatus(clock.Now.UtcDateTime));
        Assert.Equal("PC-01", unit.Name);
        Assert.Equal(2, unit.LaboratoryId);
        Assert.Equal("Student", unit.CurrentUser);
        Assert.Equal("192.0.2.1", unit.IpAddress);
    }

    [Fact]
    public void StaleHeartbeatBecomesOfflineAndFreshHeartbeatRestoresAvailability()
    {
        var clock = new Clock();
        var store = new AgentPresenceStore(clock);
        store.Connected(unitId, "connection", null);
        clock.Now = clock.Now.AddSeconds(121);
        Assert.Equal(UnitMonitoringStatus.Offline, UnitMonitoringPresence.Apply(new(), store.Snapshot()[unitId]).GetStatus(clock.Now.UtcDateTime));
        store.Heartbeat(unitId, "connection");
        Assert.Equal(UnitMonitoringStatus.Available, UnitMonitoringPresence.Apply(new(), store.Snapshot()[unitId]).GetStatus(clock.Now.UtcDateTime));
    }

    [Fact]
    public void DisconnectOverridesAnOnlineDatabaseRecordImmediately()
    {
        var clock = new Clock();
        var store = new AgentPresenceStore(clock);
        store.Connected(unitId, "connection", null);
        store.Disconnected(unitId, "connection");
        var unit = UnitMonitoringPresence.Apply(new() { IsOnline = true, LastHeartbeatUtc = clock.Now.UtcDateTime }, store.Snapshot()[unitId]);
        Assert.Equal(UnitMonitoringStatus.Offline, unit.GetStatus(clock.Now.UtcDateTime));
        store.Heartbeat(unitId, "connection");
        Assert.False(store.Snapshot()[unitId].Connected);
    }

    [Fact]
    public void OldConnectionCannotDisconnectOrRefreshItsReplacement()
    {
        var clock = new Clock();
        var store = new AgentPresenceStore(clock);
        store.Connected(unitId, "old", null);
        store.Connected(unitId, "new", null);
        var acknowledged = store.Snapshot()[unitId].HeartbeatUtc;
        clock.Now = clock.Now.AddSeconds(30);
        store.Disconnected(unitId, "old");
        store.Heartbeat(unitId, "old");
        Assert.True(store.Snapshot()[unitId].Connected);
        Assert.Equal(acknowledged, store.Snapshot()[unitId].HeartbeatUtc);
    }

    [Fact]
    public void LegacyUnitWithoutAnAgentKeepsItsDatabaseStatus()
    {
        var unit = new UnitMonitoringDTO { Id = unitId, Name = "Legacy PC" };
        Assert.Same(unit, UnitMonitoringPresence.Apply(unit, null));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
