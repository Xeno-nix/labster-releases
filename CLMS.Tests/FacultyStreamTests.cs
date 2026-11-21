using System.Security.Claims;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.UnitRegistry;
using CLMS.Domain.Entities.Booking;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.AgentConnections;
using CLMS.Web.Services.ScreenCasting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class FacultyStreamTests
{
    private const string Sdp = "v=0\r\ns=test\r\n";
    private static ClaimsPrincipal Staff(string role = "Faculty") => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Name, "Presenter"), new Claim(ClaimTypes.Role, role)], "test"));

    [Fact]
    public async Task BroadcastRequiresAnOwnedActiveMonitoringSessionForTheSameLab()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.StartAsync(new(), 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.StartAsync(Staff("Student"), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.StartAsync(f.Owner, 99));
        var stream = await f.StartAsync(f.Owner, 1);
        Assert.Equal("Lab 1", stream.Laboratory);
        Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Owner, 1, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(Staff(), 1, f.SessionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.StartAsync(Staff(), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Owner, 2, f.SessionId));
        Assert.NotNull(await f.StartAsync(Staff(), 2));
    }

    [Fact]
    public async Task OwnershipLabMembershipAndConnectionIdentityAreEnforced()
    {
        using var f = new Fixture();
        var stream = await f.StartAsync(f.Owner, 1);
        var peer = Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        var other = Staff();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PollAsync(other, stream.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.OfferAsync(other, stream.Id, peer.Id, Sdp));
        await f.Service.StopAsync(other, stream.Id);
        await f.Service.OfferAsync(f.Owner, stream.Id, peer.Id, Sdp);
        await Assert.ThrowsAsync<HubException>(() => f.Service.AnswerAsync(f.OtherUnit, "other", new(stream.Id, peer.Id, Sdp)));
        await Assert.ThrowsAsync<HubException>(() => f.Service.AnswerAsync(f.Unit, "stale", new(stream.Id, peer.Id, Sdp)));
        await f.Service.AnswerAsync(f.Unit, "agent", new(stream.Id, peer.Id, Sdp));
        Assert.Equal(Sdp, Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers).AnswerSdp);
        await Assert.ThrowsAsync<HubException>(() => f.Service.AnswerAsync(f.Unit, "agent", new(stream.Id, peer.Id, Sdp)));
    }

    [Fact]
    public async Task ReconnectedAndLateWorkstationsGetFreshPeersAndClosingViewerStaysClosed()
    {
        using var f = new Fixture();
        var stream = await f.StartAsync(f.Owner, 1);
        var first = Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        f.Connect(f.Unit, "replacement");
        var replacement = Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        Assert.NotEqual(first.Id, replacement.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.OfferAsync(f.Owner, stream.Id, first.Id, Sdp));
        await f.Service.ClosedAsync(f.Unit, "agent", stream.Id, replacement.Id);
        Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        await f.Service.ClosedAsync(f.Unit, "replacement", stream.Id, replacement.Id);
        Assert.Empty((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        var late = Guid.NewGuid();
        f.Data.Items.Add(new() { Id = late, LaboratoryId = 1, Name = "Late PC" });
        f.Connect(late, "late");
        Assert.Equal("Late PC", Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers).UnitName);
        await f.Service.StopAsync(f.Owner, stream.Id);
        var next = await f.StartAsync(f.Owner, 1);
        Assert.Equal(2, (await f.Service.PollAsync(f.Owner, next.Id)).Peers.Count);
    }

    [Fact]
    public async Task RemovedOrRevokedWorkstationsCannotReceiveAnOfferOrSupplyAnAnswer()
    {
        using var f = new Fixture();
        var stream = await f.StartAsync(f.Owner, 1);
        var peer = Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        f.Registry.Active = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.OfferAsync(f.Owner, stream.Id, peer.Id, Sdp));
        Assert.Empty((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        f.Registry.Active = true;
        peer = Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        await f.Service.OfferAsync(f.Owner, stream.Id, peer.Id, Sdp);
        f.Data.Items[0] = new() { Id = f.Unit, LaboratoryId = 2 };
        await Assert.ThrowsAsync<HubException>(() => f.Service.AnswerAsync(f.Unit, "agent", new(stream.Id, peer.Id, Sdp)));
        Assert.Empty((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
    }

    [Fact]
    public async Task BrowserLeaseCannotBeRevivedAfterExpiryAndEndedStreamsRejectOldAnswers()
    {
        using var f = new Fixture();
        var stream = await f.StartAsync(f.Owner, 1);
        var peer = Assert.Single((await f.Service.PollAsync(f.Owner, stream.Id)).Peers);
        await f.Service.OfferAsync(f.Owner, stream.Id, peer.Id, Sdp);
        f.Clock.Now += FacultyStreamService.BrowserLease;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PollAsync(f.Owner, stream.Id));
        await Assert.ThrowsAsync<HubException>(() => f.Service.AnswerAsync(f.Unit, "agent", new(stream.Id, peer.Id, Sdp)));
        await f.Service.SweepAsync();
        await f.Monitoring.SweepAsync();
        f.SessionId = 0;
        Assert.NotEqual(stream.Id, (await f.StartAsync(f.Owner, 1)).Id);
    }

    [Fact]
    public async Task DescriptionsAreBoundedAndConcurrentStartsHaveOneWinner()
    {
        using var f = new Fixture();
        var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { return await f.StartAsync(Staff(), 1); }
            catch (InvalidOperationException) { return null; }
        }));
        Assert.Single(starts, x => x is not null);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.OfferAsync(f.Owner, Guid.NewGuid(), Guid.NewGuid(), "bad"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.AnswerAsync(f.Unit, "agent",
            new(Guid.NewGuid(), Guid.NewGuid(), "v=0" + new string('x', FacultyStreamProtocol.MaximumSdpLength))));
    }

    [Fact]
    public async Task EndingMonitoringStopsItsBroadcastAndRejectsReuse()
    {
        using var f = new Fixture();
        var stream = await f.StartAsync(f.Owner, 1);
        await f.Monitoring.EndAsync(f.Owner, f.SessionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PollAsync(f.Owner, stream.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Owner, 1, f.SessionId));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        public Guid Unit { get; } = Guid.NewGuid();
        public Guid OtherUnit { get; } = Guid.NewGuid();
        public ClaimsPrincipal Owner { get; } = Staff();
        public Data Data { get; } = new();
        public Registry Registry { get; } = new();
        public Clock Clock { get; } = new();
        public AgentPresenceStore Presence { get; }
        public FacultyStreamService Service { get; }
        public LabScreenCastService Monitoring { get; }
        public int SessionId { get; set; }
        public async Task<FacultyStreamState> StartAsync(ClaimsPrincipal user, int lab)
        {
            var id = SessionId;
            if (id == 0 || user != Owner || lab != 1)
                id = await Monitoring.StartAsync(user, lab);
            if (user == Owner && lab == 1) SessionId = id;
            return await Service.StartAsync(user, lab, id);
        }
        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddLogging(); services.AddSignalR();
            services.AddSingleton<IUnitMonitoringRepository>(Data);
            services.AddSingleton<ILaboratoryRepository>(Data);
            services.AddSingleton<IUnitRegistryRepository>(Registry);
            services.AddSingleton<IScreenCastRepository, FacultyStreamHistory>();
            provider = services.BuildServiceProvider();
            Presence = new(Clock);
            Data.Items.Add(new() { Id = Unit, Name = "PC 1", LaboratoryId = 1 });
            Data.Items.Add(new() { Id = OtherUnit, Name = "Other lab PC", LaboratoryId = 2 });
            Connect(Unit, "agent"); Connect(OtherUnit, "other");
            var hub = provider.GetRequiredService<IHubContext<AgentConnectionHub>>();
            var screens = new ScreenCastService(hub, Clock, NullLogger<ScreenCastService>.Instance);
            Monitoring = new(provider.GetRequiredService<IServiceScopeFactory>(), screens, Clock, NullLogger<LabScreenCastService>.Instance);
            Service = new(provider.GetRequiredService<IServiceScopeFactory>(), Presence,
                hub, Clock, NullLogger<FacultyStreamService>.Instance, Monitoring);
        }
        public void Connect(Guid unit, string connection)
        {
            Presence.Connected(unit, connection, null);
            Presence.AssociateCredential(unit, connection, 1, Guid.NewGuid());
        }
        public void Dispose() { Service.Dispose(); Monitoring.Dispose(); provider.Dispose(); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Data : IUnitMonitoringRepository, ILaboratoryRepository
    {
        public List<UnitMonitoringDTO> Items { get; } = [];
        public Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UnitMonitoringDTO>>(Items);
        public Task<IEnumerable<Laboratory>> GetAllAsync() => Task.FromResult<IEnumerable<Laboratory>>([new() { Id = 1, Name = "Lab 1" }, new() { Id = 2, Name = "Lab 2" }]);
        public Task<Laboratory?> GetByIdAsync(int id) => Task.FromResult<Laboratory?>(id is 1 or 2 ? new() { Id = id, Name = $"Lab {id}" } : null);
    }
    private sealed class Registry : IUnitRegistryRepository
    {
        public bool Active = true;
        public Task<bool> IsAuthorizedAsync(Guid id, int generation, Guid credential, CancellationToken ct) => Task.FromResult(Active);
        public Task CleanupChallengesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<T> TransactionAsync<T>(RegistryLock key, Func<IUnitRegistryTransaction, Task<T>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegistryPage<RegistryUnit>> UnitsAsync(int? lab, string? search, int page, int size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
