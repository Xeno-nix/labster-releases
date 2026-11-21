using System.Security.Claims;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CLMS.Tests;

public sealed class ApplicationMonitoringTests
{
    [Fact]
    public async Task RegistryRequiresAdminForReadsAndEveryMutation()
    {
        var repository = new Registry(); var user = new Actor { Admin = false }; var notifications = new Notifications();
        var registry = new ApplicationRegistryService(repository, user, notifications, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.GetAllAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.SaveAsync(null, "Browser", "chrome", ApplicationPlatform.Windows, null, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.DeleteAsync(Guid.NewGuid()));
        Assert.Empty(repository.Items);
        Assert.Equal(0, notifications.Count);
    }

    [Fact]
    public async Task RegistryNormalizesRejectsDuplicatesAndNotifiesAfterPersistence()
    {
        var repository = new Registry(); var notifications = new Notifications(); var clock = new Clock();
        var registry = new ApplicationRegistryService(repository, new Actor(), notifications, clock);
        await registry.SaveAsync(null, " Browser ", " Chrome.EXE ", ApplicationPlatform.Windows, null, true);
        var app = Assert.Single(await registry.GetAllAsync());
        Assert.Equal("chrome", app.ProcessName); Assert.Equal("Browser", app.Name);
        Assert.Equal(clock.Now.UtcDateTime, app.CreatedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveAsync(null, "Other", "CHROME", ApplicationPlatform.Windows, null, true));
        await registry.SaveAsync(null, "Browser", "Chrome", ApplicationPlatform.MacOS, null, true);
        await registry.SaveAsync(app.Id, "Browser", "chrome", ApplicationPlatform.Windows, null, false);
        Assert.False(repository.Items.First(a => a.Id == app.Id).IsEnabled);
        await registry.DeleteAsync(app.Id);
        Assert.Equal(4, notifications.Count);
        Assert.Single(repository.Items);
    }

    [Theory]
    [InlineData("https://example.com/icon.png")]
    [InlineData("/application-icons/../../appsettings.json")]
    [InlineData("/application-icons/icon.png")]
    [InlineData("/application-icons/00000000000000000000000000000001.svg")]
    public async Task RegistryRejectsUnsafeIconPaths(string path)
    {
        var registry = new ApplicationRegistryService(new Registry(), new Actor(), new Notifications(), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveAsync(null, "Browser", "chrome", ApplicationPlatform.Both, path, true));
    }

    [Fact]
    public async Task ConcurrentViewersShareOneScanAndCooldownCannotBeBypassed()
    {
        var test = new Fixture();
        var first = test.Scans.RequestAsync(test.Unit);
        var second = test.Scans.RequestAsync(test.Unit);
        var request = Assert.Single(test.Hub.Proxy.Requests);
        Assert.True(test.Scans.Publish(test.Unit, "agent", new(request.RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
        Assert.Equal((await first).Response.RequestId, (await second).Response.RequestId);
        await test.Scans.RequestAsync(test.Unit);
        Assert.Single(test.Hub.Proxy.Requests);
        test.Clock.Now += TimeSpan.FromSeconds(15);
        test.Presence.Heartbeat(test.Unit, "agent");
        var next = test.Scans.RequestAsync(test.Unit);
        Assert.Equal(2, test.Hub.Proxy.Requests.Count);
        Assert.False(test.Scans.Publish(test.Unit, "agent", new(request.RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
        Assert.False(test.Scans.Publish(Guid.NewGuid(), "agent", new(test.Hub.Proxy.Requests[1].RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
        Assert.False(test.Scans.Publish(test.Unit, "replaced", new(test.Hub.Proxy.Requests[1].RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
        test.Scans.Publish(test.Unit, "agent", new(test.Hub.Proxy.Requests[1].RequestId, test.Clock.Now, ApplicationScanStatus.Success, []));
        await next;
    }

    [Fact]
    public async Task SharedScanRecordsOnceEvenIfTheInitiatingViewerDisconnects()
    {
        using var test = new Fixture();
        var app = new MonitoredApplication(Guid.NewGuid(), "Review app", "review", ApplicationPlatform.Windows, null, true,
            test.Clock.Now.UtcDateTime.AddMinutes(-1), test.Clock.Now.UtcDateTime.AddMinutes(-1)) { FlagForReview = true };
        test.Registry.Items.Add(app);
        var monitoring = new ApplicationMonitoringService(new Units(test.Unit), test.Registry, test.Scans, new TestCurrentUser());
        using var cancellation = new CancellationTokenSource();
        var viewer = monitoring.RequestAsync(Viewer("Faculty"), test.Unit, cancellation.Token);
        var otherViewer = test.Scans.RequestAsync(test.Unit);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewer);
        var response = new ApplicationScanResponse(Assert.Single(test.Hub.Proxy.Requests).RequestId, test.Clock.Now,
            ApplicationScanStatus.Success, [app.Id]) { RegistryRevision = ApplicationRegistryRevision.Compute(test.Registry.Items) };
        Assert.True(test.Scans.Publish(test.Unit, "agent", response));
        Assert.Equal(ApplicationScanStatus.Success, (await otherViewer).Response.Status);
        Assert.Equal(1, test.Activities.Observations); Assert.Single(test.Activities.Items);
        await test.Scans.RequestAsync(test.Unit);
        Assert.Equal(1, test.Activities.Observations); Assert.Equal(1, test.Changes.Count);
        Assert.False(test.Scans.Publish(test.Unit, "agent", response));
    }

    [Fact]
    public async Task DetectedReviewFlagsAreResolvedByTheServerAndSortedFirst()
    {
        using var test = new Fixture();
        var flagged = new MonitoredApplication(Guid.NewGuid(), "Z review app", "review", ApplicationPlatform.Windows, null, true, default, default) { FlagForReview = true };
        var normal = flagged with { Id = Guid.NewGuid(), Name = "A normal app", ProcessName = "normal", FlagForReview = false };
        test.Registry.Items.AddRange([normal, flagged]);
        var service = new ApplicationMonitoringService(new Units(test.Unit), test.Registry, test.Scans, new TestCurrentUser());
        var task = service.RequestAsync(Viewer("Faculty"), test.Unit);
        test.Scans.Publish(test.Unit, "agent", new(Assert.Single(test.Hub.Proxy.Requests).RequestId, test.Clock.Now, ApplicationScanStatus.Success, [normal.Id, flagged.Id]));
        var result = await task;
        Assert.True(result.Applications[0].FlagForReview); Assert.Equal(flagged.Id, result.Applications[0].Id);
        Assert.False(result.Applications[1].FlagForReview);
    }

    [Fact]
    public async Task OfflineAndDisconnectedScansAreUnavailable()
    {
        var test = new Fixture();
        var pending = test.Scans.RequestAsync(test.Unit);
        test.Presence.Disconnected(test.Unit, "agent");
        test.Scans.Disconnected(test.Unit, "agent");
        Assert.Equal(ApplicationScanStatus.AgentUnavailable, (await pending).Response.Status);
        Assert.Equal(ApplicationScanStatus.AgentUnavailable, (await test.Scans.RequestAsync(test.Unit)).Response.Status);
        Assert.False(test.Scans.Publish(test.Unit, "agent", new(test.Hub.Proxy.Requests[0].RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
    }

    [Fact]
    public async Task ReplacementConnectionInvalidatesOldRepliesAndPreservesUnitCooldown()
    {
        var test = new Fixture();
        var previous = test.Scans.RequestAsync(test.Unit);
        var request = Assert.Single(test.Hub.Proxy.Requests);
        test.Presence.Connected(test.Unit, "replacement", null);
        test.Presence.Heartbeat(test.Unit, "replacement");
        test.Scans.Register(test.Unit, "replacement", ApplicationPlatform.Windows);
        Assert.Equal(ApplicationScanStatus.AgentUnavailable, (await previous).Response.Status);
        Assert.False(test.Scans.Publish(test.Unit, "agent", new(request.RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
        await test.Scans.RequestAsync(test.Unit);
        Assert.Single(test.Hub.Proxy.Requests);
        test.Clock.Now += ApplicationScanCoordinator.Cooldown;
        var next = test.Scans.RequestAsync(test.Unit);
        Assert.Equal(2, test.Hub.Proxy.Requests.Count);
        test.Scans.Publish(test.Unit, "replacement", new(test.Hub.Proxy.Requests[1].RequestId, test.Clock.Now, ApplicationScanStatus.Success, []));
        await next;
    }

    [Fact]
    public async Task TimeoutAndLateRepliesCannotReplaceTheNextScan()
    {
        var test = new Fixture();
        var pending = test.Scans.RequestAsync(test.Unit);
        test.Clock.FireTimers();
        Assert.Equal(ApplicationScanStatus.TimedOut, (await pending).Response.Status);
        Assert.False(test.Scans.Publish(test.Unit, "agent", new(test.Hub.Proxy.Requests[0].RequestId, test.Clock.Now, ApplicationScanStatus.Success, [])));
    }

    [Fact]
    public async Task MonitoringChecksStaffAndUnitAccessAndResolvesOnlyEnabledApplicableDetections()
    {
        var test = new Fixture(); var repository = new Registry();
        var detected = Guid.NewGuid(); var disabled = Guid.NewGuid(); var mac = Guid.NewGuid();
        repository.Items.AddRange([
            new(detected, "Browser", "chrome", ApplicationPlatform.Windows, null, true, default, default),
            new(disabled, "Disabled", "helper", ApplicationPlatform.Both, null, false, default, default),
            new(mac, "Mac app", "mac", ApplicationPlatform.MacOS, null, true, default, default),
            new(Guid.NewGuid(), "Not detected", "other", ApplicationPlatform.Both, null, true, default, default)]);
        var service = new ApplicationMonitoringService(new Units(test.Unit), repository, test.Scans, new TestCurrentUser());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(new ClaimsPrincipal(), test.Unit));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(Viewer("Student"), test.Unit));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(Viewer("Faculty"), Guid.NewGuid()));
        Assert.Empty(test.Hub.Proxy.Requests);
        var pending = service.RequestAsync(Viewer("Faculty"), test.Unit);
        test.Scans.Publish(test.Unit, "agent", new(Assert.Single(test.Hub.Proxy.Requests).RequestId, test.Clock.Now,
            ApplicationScanStatus.Success, [detected, detected, disabled, mac, Guid.NewGuid()]));
        var result = await pending;
        var app = Assert.Single(result.Applications);
        Assert.Equal("Browser", app.Name); Assert.Equal(ApplicationProcessNames.DefaultIcon, app.IconPath);
        repository.Items.Clear();
        Assert.Empty((await service.RequestAsync(Viewer("Admin"), test.Unit)).Applications);
    }

    [Fact]
    public void UploadRejectsSpoofedTypesAndOversizedImages()
    {
        Assert.Throws<ArgumentException>(() => ApplicationIconStorage.ValidateImage("<svg></svg>"u8.ToArray(), "image/png"));
        Assert.Throws<ArgumentException>(() => ApplicationIconStorage.ValidateImage(new byte[ApplicationIconStorage.MaximumBytes + 1], "image/webp"));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        Assert.Equal(".png", ApplicationIconStorage.ValidateImage(png, "image/png"));
        Assert.Throws<ArgumentException>(() => ApplicationIconStorage.ValidateImage(png, "image/webp"));
    }

    private static ClaimsPrincipal Viewer(string role) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "test"));

    internal sealed class Registry : IApplicationRegistryRepository
    {
        public List<MonitoredApplication> Items = [];
        public Task<IReadOnlyList<MonitoredApplication>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MonitoredApplication>>(Items.ToArray());
        public Task SaveAsync(MonitoredApplication app, CancellationToken ct = default) { Items.RemoveAll(a => a.Id == app.Id); Items.Add(app); return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken ct = default) { Items.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
    }
    private sealed class Actor : ICurrentUser
    {
        public bool Admin = true;
        public Task<Guid> RequirePrivilegeAsync(string privilege) => RequireAdminAsync();
        public Task<Guid> RequireAdminAsync() => Admin ? Task.FromResult(Guid.NewGuid()) : throw new UnauthorizedAccessException();
    }
    private sealed class Notifications : IApplicationRegistryNotifications
    {
        public int Count;
        public Task RegistryChangedAsync(CancellationToken ct = default) { Count++; return Task.CompletedTask; }
    }
    private sealed class Units(Guid unit) : IUnitMonitoringRepository
    {
        public Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnitMonitoringDTO>>([new() { Id = unit, LaboratoryId = 1 }]);
    }
    private sealed class Fixture : IDisposable
    {
        public Guid Unit = Guid.NewGuid();
        public Clock Clock = new();
        public Hub Hub = new();
        public AgentPresenceStore Presence;
        public ApplicationScanCoordinator Scans;
        public Registry Registry = new();
        public ActivityTestRepository Activities;
        public ActivityTestNotifications Changes = new();
        private readonly ServiceProvider provider;
        public Fixture()
        {
            Presence = new(Clock); Presence.Connected(Unit, "agent", null); Presence.Heartbeat(Unit, "agent");
            Activities = new(Registry);
            var services = new ServiceCollection();
            services.AddSingleton<IAbnormalActivityRepository>(Activities);
            services.AddSingleton<IUnitMonitoringRepository>(new Units(Unit));
            services.AddSingleton<IAbnormalActivityNotifications>(Changes);
            services.AddSingleton<ICurrentUser>(new TestCurrentUser());
            services.AddScoped<AbnormalActivityService>();
            provider = services.BuildServiceProvider();
            Scans = new(Hub, Presence, Clock, NullLogger<ApplicationScanCoordinator>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(), Changes);
            Scans.Register(Unit, "agent", ApplicationPlatform.Windows);
        }
        public void Dispose() => provider.Dispose();
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
        private readonly List<TestTimer> timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(() => callback(state)); timers.Add(timer); return timer;
        }
        public void FireTimers() { foreach (var timer in timers.ToArray()) timer.Fire(); }
        private sealed class TestTimer(Action callback) : ITimer
        {
            private bool disposed;
            public void Fire() { if (!disposed) callback(); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Proxy : IClientProxy
    {
        public List<ApplicationScanRequest> Requests = [];
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == ApplicationMonitoringProtocol.Scan) Requests.Add((ApplicationScanRequest)args[0]!);
            return Task.CompletedTask;
        }
    }
    private sealed class Hub : IHubContext<AgentConnectionHub>, IHubClients
    {
        public Proxy Proxy = new();
        IHubClients IHubContext<AgentConnectionHub>.Clients => this;
        IGroupManager IHubContext<AgentConnectionHub>.Groups => throw new NotSupportedException();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }
}
