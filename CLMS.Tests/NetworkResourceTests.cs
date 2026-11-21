using CLMS.Agent.Services;
using CLMS.Shared.Dtos;
using CLMS.Web.Models;
using CLMS.Web.Services.AgentConnections;
using Xunit;

namespace CLMS.Tests;

public sealed class NetworkResourceTests
{
    [Fact]
    public void RatesUseElapsedTimeAndExcludeNewOrResetAdapters()
    {
        var clock = new Clock();
        IReadOnlyList<NetworkByteCounters> counters = [new("ethernet", 1000, 2000), new("wifi", 500, 500)];
        var sampler = new NetworkResourceSampler(() => counters, clock);
        Assert.Null(sampler.Sample());
        clock.Timestamp += 10;
        counters = [new("ethernet", 4000, 3000), new("wifi", 100, 100), new("new", 999999, 999999)];
        Assert.Equal(new NetworkResourceReading(300, 100), sampler.Sample());
        clock.Timestamp += 10;
        Assert.Equal(new NetworkResourceReading(0, 0), sampler.Sample());
        sampler.Reset();
        Assert.Null(sampler.Sample());
    }

    [Fact]
    public void MissingAdaptersRequireANewBaselineWhenTheyReturn()
    {
        var clock = new Clock();
        IReadOnlyList<NetworkByteCounters> counters = [new("ethernet", 100, 100)];
        var sampler = new NetworkResourceSampler(() => counters, clock);
        sampler.Sample();
        clock.Timestamp++;
        counters = [];
        Assert.Null(sampler.Sample());
        clock.Timestamp++;
        counters = [new("ethernet", 10000, 10000)];
        Assert.Null(sampler.Sample());
    }

    [Fact]
    public void ReadingsBelongToCurrentConnectionAndFlowToMonitoring()
    {
        var clock = new Clock();
        var store = new AgentPresenceStore(clock);
        var unitId = Guid.NewGuid();
        store.Connected(unitId, "old", null);
        store.ReportNetworkResources(unitId, "old", new(1234, 5678));
        var unit = UnitMonitoringPresence.Apply(new() { Id = unitId }, store.Snapshot()[unitId]);
        Assert.Equal(new NetworkResourceReading(1234, 5678), unit.NetworkResources);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, unit.NetworkResourcesUtc);
        store.Connected(unitId, "new", null);
        store.ReportNetworkResources(unitId, "old", new(999, 999));
        Assert.Null(store.Snapshot()[unitId].NetworkResources);
        store.ReportNetworkResources(unitId, "new", new(-1, 0));
        Assert.Null(store.Snapshot()[unitId].NetworkResources);
        store.Disconnected(unitId, "new");
        store.ReportNetworkResources(unitId, "new", new(999, 999));
        Assert.Null(store.Snapshot()[unitId].NetworkResources);
    }

    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Timestamp);
    }
}
