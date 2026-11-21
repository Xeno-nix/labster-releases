using System.Security.Claims;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Web.Hubs;
using CLMS.Web.Services.ScreenCasting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class LabScreenCastTests
{
    [Fact]
    public async Task SessionContainsOnlySelectedLabAndDoesNotCaptureUntilViewed()
    {
        using var test = new Fixture();
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        Assert.Equal(1, id);
        Assert.Equal(test.ViewerId, test.History.Owner);
        Assert.Equal(new[] { test.Unit }, test.History.Units);
        Assert.Empty(test.History.Connections);
        Assert.Equal(1, test.History.Recoveries);
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Labs.StartAsync(test.OtherViewer, 1));
        await test.Labs.StartAsync(test.OtherViewer, 2);
        Assert.Equal(1, test.History.Recoveries);
    }

    [Fact]
    public async Task OwnershipAndLabMembershipAreEnforcedOnServer()
    {
        using var test = new Fixture();
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        Assert.False(await test.Labs.RenewAsync(test.OtherViewer, id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Labs.StartViewingAsync(test.OtherViewer, id, test.Unit));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => test.Labs.StartViewingAsync(test.Viewer, id, test.OtherUnit));
        await test.Labs.EndAsync(test.OtherViewer, id);
        Assert.True(await test.Labs.RenewAsync(test.Viewer, id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => test.Labs.StartAsync(new ClaimsPrincipal(), 1));
    }

    [Fact]
    public async Task PagingStopsOnlyPreviewAndEndingSessionStopsRemainingCaptures()
    {
        using var test = new Fixture();
        await test.Screens.AgentConnectedAsync(test.Unit, "agent");
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        var capture = await test.Labs.StartViewingAsync(test.Viewer, id, test.Unit);
        Assert.True(test.History.Connections[test.Unit]);
        await test.Labs.StopViewingAsync(test.Viewer, id, capture);
        Assert.False(test.Screens.IsActive(test.Viewer, capture));
        Assert.False(test.History.Connections[test.Unit]);
        Assert.True(await test.Labs.RenewAsync(test.Viewer, id));
        var next = await test.Labs.StartViewingAsync(test.Viewer, id, test.Unit);
        await test.Labs.EndAsync(test.Viewer, id);
        Assert.False(test.Screens.IsActive(test.Viewer, next));
        Assert.Equal((id, false), Assert.Single(test.History.Ended));
        Assert.False(await test.Labs.RenewAsync(test.Viewer, id));
    }

    [Fact]
    public async Task MissingBrowserLeaseFailsSessionEvenIfNoPcIsOnline()
    {
        using var test = new Fixture();
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        test.Clock.Now += ScreenCastService.ViewerLease + TimeSpan.FromSeconds(1);
        Assert.False(await test.Labs.RenewAsync(test.Viewer, id));
        await test.Labs.SweepAsync();
        Assert.Equal((id, true), Assert.Single(test.History.Ended));
    }

    [Fact]
    public async Task AgentDisconnectAndLocalStopUpdateConnectionHistory()
    {
        using var test = new Fixture();
        await test.Screens.AgentConnectedAsync(test.Unit, "agent");
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        await test.Labs.StartViewingAsync(test.Viewer, id, test.Unit);
        await test.Screens.AgentDisconnectedAsync(test.Unit, "agent");
        await test.Labs.SweepAsync();
        Assert.False(test.History.Connections[test.Unit]);
        Assert.Empty(test.History.Ended);
        await test.Screens.AgentConnectedAsync(test.Unit, "replacement");
        var capture = await test.Labs.StartViewingAsync(test.Viewer, id, test.Unit);
        test.Screens.CaptureStopped(test.Unit, "replacement", capture);
        await test.Labs.SweepAsync();
        Assert.False(test.History.Connections[test.Unit]);
    }

    [Fact]
    public async Task DatabaseFailureStopsCaptureAndEndWriteIsRetried()
    {
        using var test = new Fixture();
        await test.Screens.AgentConnectedAsync(test.Unit, "agent");
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        var capture = await test.Labs.StartViewingAsync(test.Viewer, id, test.Unit);
        test.History.FailEnd = true;
        await Assert.ThrowsAsync<IOException>(() => test.Labs.EndAsync(test.Viewer, id));
        Assert.False(test.Screens.IsActive(test.Viewer, capture));
        Assert.False(await test.Labs.RenewAsync(test.Viewer, id));
        test.History.FailEnd = false;
        await test.Labs.SweepAsync();
        Assert.Equal((id, false), Assert.Single(test.History.Ended));
    }

    [Fact]
    public async Task PcMovedToAnotherLabCannotBeViewedThroughOldSession()
    {
        using var test = new Fixture();
        var id = await test.Labs.StartAsync(test.Viewer, 1);
        test.Units.Items[0] = new() { Id = test.Unit, LaboratoryId = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Labs.StartViewingAsync(test.Viewer, id, test.Unit));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        public Guid Unit { get; } = Guid.NewGuid();
        public Guid OtherUnit { get; } = Guid.NewGuid();
        public Guid ViewerId { get; } = Guid.NewGuid();
        public ClaimsPrincipal Viewer { get; }
        public ClaimsPrincipal OtherViewer { get; } = Principal(Guid.NewGuid());
        public Clock Clock { get; } = new();
        public History History { get; } = new();
        public Units Units { get; } = new();
        public ScreenCastService Screens { get; }
        public LabScreenCastService Labs { get; }

        public Fixture()
        {
            Viewer = Principal(ViewerId);
            Units.Items.Add(new() { Id = Unit, LaboratoryId = 1 });
            Units.Items.Add(new() { Id = OtherUnit, LaboratoryId = 2 });
            var services = new ServiceCollection();
            services.AddLogging(); services.AddSingleton<CLMS.Application.Identity.Interfaces.ICurrentUser>(new TestCurrentUser()); services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TestPrivilegeHandler>();
            services.AddSignalR();
            services.AddSingleton<IScreenCastRepository>(History);
            services.AddSingleton<IUnitMonitoringRepository>(Units);
            provider = services.BuildServiceProvider();
            Screens = new(provider.GetRequiredService<IHubContext<AgentConnectionHub>>(), Clock, NullLogger<ScreenCastService>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
            Labs = new(provider.GetRequiredService<IServiceScopeFactory>(), Screens, Clock, NullLogger<LabScreenCastService>.Instance);
        }

        public void Dispose() { Labs.Dispose(); Screens.Dispose(); provider.Dispose(); }
        private static ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
    }

    private sealed class Units : IUnitMonitoringRepository
    {
        public List<UnitMonitoringDTO> Items { get; } = [];
        public Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UnitMonitoringDTO>>(Items);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class History : IScreenCastRepository
    {
        private int nextId;
        public int Recoveries { get; private set; }
        public Guid Owner { get; private set; }
        public Guid[] Units { get; private set; } = [];
        public Dictionary<Guid, bool> Connections { get; } = [];
        public List<(int, bool)> Ended { get; } = [];
        public bool FailEnd { get; set; }
        public Task<int> CreateAsync(Guid initiatedBy, IReadOnlyCollection<Guid> units, DateTime startedAt)
        {
            Owner = initiatedBy;
            Units = units.ToArray();
            return Task.FromResult(++nextId);
        }
        public Task SetConnectionAsync(int sessionId, Guid unitId, bool connected, DateTime at)
        {
            Connections[unitId] = connected;
            return Task.CompletedTask;
        }
        public Task EndAsync(int sessionId, bool failed, DateTime endedAt)
        {
            if (FailEnd) throw new IOException("Database unavailable");
            Ended.Add((sessionId, failed));
            return Task.CompletedTask;
        }
        public Task RecoverInterruptedAsync(DateTime endedAt) { Recoveries++; return Task.CompletedTask; }
    }
}
