using CLMS.Application.UnitRegistry;
using CLMS.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using CLMS.Web.Controllers;
using CLMS.Web.Services.ScreenCasting;
using CLMS.Agent.Services;
using CLMS.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentConnectionTests
{
    private static readonly Guid UnitId = Guid.Parse("5db3ce75-930c-4a77-834a-a5c9a41d0538");
    private static readonly Guid CredentialId = Guid.Parse("00000000-0000-0000-0000-000000000077");
    private const string SigningKey = "test-signing-key-64-characters-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string DeviceKey = new AgentTokenService(
        Options.Create(new JwtOptions { Issuer = "hub-tests", Key = SigningKey, SigningAlgorithm = "HS256" }),
        Options.Create(new AgentAuthOptions())).Issue(UnitId, 1, CredentialId, DateTime.UtcNow).AccessToken;

    [Fact]
    public async Task ValidDeviceReceivesItsServerAssignedIdentityAndHeartbeat()
    {
        await using var app = await StartServerAsync();
        await using var connection = CreateConnection(app, DeviceKey);
        await connection.StartAsync();

        var info = await connection.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);

        Assert.Equal(UnitId, info.ComputerUnitId);
        Assert.Equal(connection.ConnectionId, info.ConnectionId);
        var presence = app.Services.GetRequiredService<AgentPresenceStore>().Snapshot()[UnitId];
        Assert.True(presence.Connected);
        Assert.Equal(connection.ConnectionId, presence.ConnectionId);
        Assert.InRange(presence.HeartbeatUtc, info.ServerTimeUtc.UtcDateTime.AddSeconds(-1), info.ServerTimeUtc.UtcDateTime);
        Assert.InRange(info.ServerTimeUtc, DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow.AddSeconds(10));
        await connection.StopAsync();
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-different-device-key-at-least-32-characters")]
    public async Task MissingOrWrongDeviceCredentialIsRejected(string? key)
    {
        await using var app = await StartServerAsync();
        await using var connection = CreateConnection(app, key);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task RevokedDeviceCannotConnectWithAnOtherwiseValidJwt()
    {
        await using var app = await StartServerAsync(provisionDevice: false);
        await using var connection = CreateConnection(app, DeviceKey);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task StaleCredentialGenerationCannotConnect()
    {
        await using var app = await StartServerAsync(duplicateKey: true);
        await using var connection = CreateConnection(app, DeviceKey);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizedViewerCanReceiveALargeFrameAndStopCapture()
    {
        await using var app = await StartServerAsync();
        await using var agent = CreateConnection(app, DeviceKey);
        var start = new TaskCompletionSource<ScreenCastCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new TaskCompletionSource<ScreenCastCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        agent.On<ScreenCastCommand>(ScreenCastProtocol.StartCapture, command => start.TrySetResult(command));
        agent.On<ScreenCastCommand>(ScreenCastProtocol.StopCapture, command => stop.TrySetResult(command));
        await agent.StartAsync();
        await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var service = app.Services.GetRequiredService<ScreenCastService>();
        var viewer = Viewer("owner");
        var session = await service.StartAsync(viewer, UnitId);
        Assert.Equal(session, (await start.Task.WaitAsync(TimeSpan.FromSeconds(5))).SessionId);
        var frame = Frame(session, large: true);
        Assert.True(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, frame));

        using var browser = await CreateBrowserAsync(app, "owner", "Faculty");
        var response = await browser.GetAsync($"/api/screen-cast/{session}/frame");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(frame.JpegBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await browser.GetAsync($"/api/screen-cast/{session}/frame?after=1")).StatusCode);

        await service.StopAsync(viewer, session);
        Assert.Equal(session, (await stop.Task.WaitAsync(TimeSpan.FromSeconds(5))).SessionId);
        Assert.False(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, frame));
    }

    [Fact]
    public async Task OtherViewersAndUnauthenticatedRequestsCannotReadOrStopASession()
    {
        await using var app = await StartServerAsync();
        await using var agent = CreateConnection(app, DeviceKey);
        await agent.StartAsync();
        await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var service = app.Services.GetRequiredService<ScreenCastService>();
        var session = await service.StartAsync(Viewer("owner"), UnitId);
        Assert.True(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, Frame(session)));

        using var stranger = await CreateBrowserAsync(app, "another-user", "Admin");
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/screen-cast/{session}/frame")).StatusCode);
        await service.StopAsync(Viewer("another-user"), session);
        Assert.True(service.TryGetFrame(Viewer("owner"), session, 0, out _));
        using var anonymous = app.GetTestClient();
        anonymous.BaseAddress = new Uri("https://localhost");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/screen-cast/{session}/frame")).StatusCode);
        using var unprivileged = await CreateBrowserAsync(app, "someone", "Student");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await unprivileged.GetAsync($"/api/screen-cast/{session}/frame")).StatusCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StartAsync(Viewer("someone", "Student"), UnitId));
    }

    [Fact]
    public async Task FramesRequireTheAuthorizedUnitAndConnection()
    {
        await using var app = await StartServerAsync();
        await using var agent = CreateConnection(app, DeviceKey);
        await agent.StartAsync();
        var info = await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var service = app.Services.GetRequiredService<ScreenCastService>();
        var session = await service.StartAsync(Viewer("owner"), UnitId);
        Assert.False(service.Publish(Guid.NewGuid(), info.ConnectionId, Frame(session)));
        Assert.False(service.Publish(UnitId, "another-connection", Frame(session)));
        Assert.False(service.Publish(UnitId, info.ConnectionId, Frame(Guid.NewGuid())));
        var frame = Frame(session);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() =>
            agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, frame with { Width = 1280 }));
        Assert.True(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, frame));
    }

    [Fact]
    public async Task MissingBrowserExpiresTheLeaseAndStopsTheAgent()
    {
        var clock = new TestClock();
        await using var app = await StartServerAsync(clock: clock);
        await using var agent = CreateConnection(app, DeviceKey);
        var stopped = new TaskCompletionSource<ScreenCastCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        agent.On<ScreenCastCommand>(ScreenCastProtocol.StopCapture, command => stopped.TrySetResult(command));
        await agent.StartAsync();
        await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var service = app.Services.GetRequiredService<ScreenCastService>();
        var session = await service.StartAsync(Viewer("owner"), UnitId);
        clock.Now += ScreenCastService.ViewerLease + TimeSpan.FromSeconds(1);
        await service.ExpireSessionsAsync();
        Assert.Equal(session, (await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5))).SessionId);
        Assert.False(service.TryGetFrame(Viewer("owner"), session, 0, out _));
        Assert.False(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, Frame(session)));
    }

    [Fact]
    public async Task AgentStoppingOrReconnectingDoesNotResumeAnOldSession()
    {
        await using var app = await StartServerAsync();
        await using var agent = CreateConnection(app, DeviceKey);
        await agent.StartAsync();
        await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var service = app.Services.GetRequiredService<ScreenCastService>();
        var viewer = Viewer("owner");
        var session = await service.StartAsync(viewer, UnitId);
        await agent.InvokeAsync(ScreenCastProtocol.CaptureStopped, session);
        Assert.False(service.TryGetFrame(viewer, session, 0, out _));
        var secondSession = await service.StartAsync(viewer, UnitId);
        // A replacement connection for the same device must invalidate the previous stream.
        await using var replacement = CreateConnection(app, DeviceKey);
        await replacement.StartAsync();
        await replacement.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        Assert.False(service.TryGetFrame(viewer, secondSession, 0, out _));
        Assert.False(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, Frame(secondSession)));
    }

    [Fact]
    public void InvalidAndOversizedJpegsAreRejected()
    {
        var frame = Frame(Guid.NewGuid());
        Assert.True(JpegFrameValidator.IsValid(frame));
        Assert.False(JpegFrameValidator.IsValid(frame with { JpegBytes = new byte[ScreenCastProtocol.MaximumFrameBytes + 1] }));
        Assert.False(JpegFrameValidator.IsValid(frame with { Width = 1281 }));
        Assert.False(JpegFrameValidator.IsValid(frame with { JpegBytes = [1, 2, 3] }));
    }

    private static ClaimsPrincipal Viewer(string id, string role = "Faculty") => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, Guid.TryParse(id, out var guid) ? guid.ToString() : new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)).AsSpan(0, 16)).ToString()), new Claim(ClaimTypes.Role, role)], "TestViewer"));

    private static ScreenCastFrame Frame(Guid session, bool large = false)
    {
        var jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "screen-frame.jpg"));
        if (large)
        {
            // JPEG comments exercise uploads beyond both the default 32 KB message and 64 KB buffer.
            var expanded = new byte[jpeg.Length + 3 * 50004];
            expanded[0] = 0xff; expanded[1] = 0xd8;
            for (var index = 0; index < 3; index++)
            {
                var offset = 2 + index * 50004;
                expanded[offset] = 0xff; expanded[offset + 1] = 0xfe;
                expanded[offset + 2] = (byte)(50002 >> 8); expanded[offset + 3] = (byte)(50002 & 255);
            }
            jpeg.AsSpan(2).CopyTo(expanded.AsSpan(2 + 3 * 50004));
            jpeg = expanded;
        }
        return new(session, jpeg, 1, 1);
    }

    [Fact]
    public async Task SharedSenderRespondsToStartAndStopCommandsAndDeliversCapture()
    {
        await using var app = await StartServerAsync();
        await using var agent = CreateConnection(app, DeviceKey);
        using var lifetime = new CancellationTokenSource();
        var sender = new ScreenCastSender(new TestCapture(), NullLogger<ScreenCastSender>.Instance);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        agent.On<ScreenCastCommand>(ScreenCastProtocol.StartCapture, command => sender.StartAsync(agent, command.SessionId, lifetime.Token));
        agent.On<ScreenCastCommand>(ScreenCastProtocol.StopCapture, async command =>
        {
            await sender.StopAsync(command.SessionId);
            stopped.TrySetResult();
        });
        await agent.StartAsync();
        await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var service = app.Services.GetRequiredService<ScreenCastService>();
        var viewer = Viewer("owner");
        var session = await service.StartAsync(viewer, UnitId);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ScreenCastFrameSnapshot? received = null;
            while (received is null)
            {
                timeout.Token.ThrowIfCancellationRequested();
                service.TryGetFrame(viewer, session, 0, out received);
                if (received is null) await Task.Delay(20, timeout.Token);
            }
            Assert.Equal(Frame(session).JpegBytes, received.JpegBytes);
            Assert.True(sender.IsSharing);
            await service.StopAsync(viewer, session);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(sender.IsSharing);
        }
        finally
        {
            lifetime.Cancel();
            await sender.StopAsync();
        }
    }

    private sealed class TestCapture : IScreenCaptureService
    {
        public Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ScreenFrame(Frame(Guid.Empty).JpegBytes, 1, 1, DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task ApplicationRegistrySynchronizesAtStartupAfterChangesAndAfterReconnectWithoutPollingProcesses()
    {
        await using var app = await StartServerAsync();
        var registry = (ApplicationMonitoringTests.Registry)app.Services.GetRequiredService<CLMS.Application.Monitoring.IApplicationRegistryRepository>();
        var id = Guid.NewGuid();
        registry.Items.Add(new(id, "Browser", "Chrome.EXE", CLMS.Domain.Entities.UnitManagement.ApplicationPlatform.Windows,
            null, true, DateTime.UtcNow, DateTime.UtcNow));
        registry.Items.Add(new(Guid.NewGuid(), "Other platform", "chrome", CLMS.Domain.Entities.UnitManagement.ApplicationPlatform.MacOS,
            null, true, DateTime.UtcNow, DateTime.UtcNow));
        var enumerations = 0;
        var monitor = new ApplicationProcessMonitor(CLMS.Domain.Entities.UnitManagement.ApplicationPlatform.Windows,
            () => { Interlocked.Increment(ref enumerations); return ["CHROME", "chrome", "unregistered"]; });
        var sender = new ScreenCastSender(new TestCapture(), NullLogger<ScreenCastSender>.Instance);
        await using var transport = new AgentConnectionService(NullLogger<AgentConnectionService>.Instance, sender,
            new FixedTokens(), (uri, token) => new HubConnectionBuilder().WithUrl(uri, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
                options.AccessTokenProvider = token;
            }).Build(), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5), monitor);
        await transport.ConnectAsync("https://localhost", DeviceKey);
        Assert.True(monitor.IsInitialized);
        Assert.Equal(0, enumerations);
        var scans = app.Services.GetRequiredService<ApplicationScanCoordinator>();
        var scan = await scans.RequestAsync(UnitId);
        Assert.Equal(ApplicationScanStatus.Success, scan.Response.Status);
        Assert.Equal(id, Assert.Single(scan.Response.ApplicationIds));
        Assert.Equal(1, enumerations);

        registry.Items[0] = registry.Items[0] with { IsEnabled = false };
        await scans.RegistryChangedAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (monitor.Scan(Guid.NewGuid()).ApplicationIds.Count != 0) await Task.Delay(10, deadline.Token);

        registry.Items[0] = registry.Items[0] with { IsEnabled = true };
        // No notification: the next authenticated connection must recover this missed change.
        await transport.ConnectAsync("https://localhost", DeviceKey);
        Assert.Equal(id, Assert.Single(monitor.Scan(Guid.NewGuid()).ApplicationIds));
    }

    private sealed class FixedTokens : IAgentAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(DeviceKey);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<System.Net.Http.HttpClient> CreateBrowserAsync(WebApplication app, string id, string role)
    {
        var browser = app.GetTestClient();
        browser.BaseAddress = new Uri("https://localhost");
        var response = await browser.PostAsync($"/test-login/{id}/{role}", new StringContent(""));
        response.EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        return browser;
    }

    private static async Task<WebApplication> StartServerAsync(bool provisionDevice = true, bool duplicateKey = false, TimeProvider? clock = null)
    {
        // Uses only the real hub/auth components; no MySQL or staff credentials are needed.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        // A TestServer needs neither Windows Event Log access nor persistent OS keys.
        builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hub-tests", ["Jwt:Audience"] = "humans", ["Jwt:Key"] = SigningKey,
            ["Jwt:SigningAlgorithm"] = "HS256"
        });
        builder.Services.AddSingleton<IUnitRegistryRepository>(new HubRegistry { Active = provisionDevice, Generation = duplicateKey ? 2 : 1 });
        if (clock is not null) builder.Services.AddSingleton(clock);
        builder.Services.AddAgentConnections(builder.Configuration);
        builder.Services.AddSingleton<CLMS.Application.Identity.Interfaces.ICurrentUser>(new TestCurrentUser());
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TestPrivilegeHandler>();
        builder.Services.AddSingleton<CLMS.Application.Monitoring.IApplicationRegistryRepository, ApplicationMonitoringTests.Registry>();
        builder.Services.AddScoped<CLMS.Application.Monitoring.AbnormalActivityService>();
        builder.Services.AddSingleton<CLMS.Application.Interfaces.Repositories.IUnitMonitoringRepository>(new ActivityTestUnits(UnitId));
        builder.Services.AddSingleton<CLMS.Application.Monitoring.IAbnormalActivityRepository>(sp =>
            new ActivityTestRepository(sp.GetRequiredService<CLMS.Application.Monitoring.IApplicationRegistryRepository>()));
        builder.Services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
        builder.Services.AddControllers().AddApplicationPart(typeof(ScreenCastFramesController).Assembly);
        builder.Services.AddAntiforgery();
        var app = builder.Build();
        app.UseWhen(context => !context.Request.Path.StartsWithSegments(AgentConnectionProtocol.HubPath)
            && !context.Request.Path.StartsWithSegments(ScreenCastProtocol.FrameRoutePrefix), branch =>
            branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapHub<AgentConnectionHub>(AgentConnectionProtocol.HubPath,
            options =>
            {
                options.CloseOnAuthenticationExpiration = true;
                options.ApplicationMaxBufferSize = ScreenCastProtocol.MaximumHubMessageBytes;
            }).DisableAntiforgery();
        app.MapControllers();
        app.MapPost("/test-login/{id}/{role}", async (HttpContext context, string id, string role) =>
        {
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Viewer(id, role));
            return Results.NoContent();
        });
        // If a negotiate failure is incorrectly re-executed, this would replace its 401.
        app.MapPost("/not-found", () => Results.StatusCode(StatusCodes.Status418ImATeapot));
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task RevocationOnAnEstablishedConnectionRemovesCaptureAndPresence()
    {
        await using var app = await StartServerAsync();
        await using var agent = CreateConnection(app, DeviceKey);
        await agent.StartAsync();
        await agent.InvokeAsync<AgentConnectionInfo>(AgentConnectionProtocol.HeartbeatMethod);
        var screens = app.Services.GetRequiredService<ScreenCastService>();
        var viewer = Viewer("owner");
        var session = await screens.StartAsync(viewer, UnitId);
        Assert.True(await agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, Frame(session)));
        ((HubRegistry)app.Services.GetRequiredService<IUnitRegistryRepository>()).Active = false;
        await Assert.ThrowsAnyAsync<Exception>(() => agent.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame, Frame(session)));
        using var browser = await CreateBrowserAsync(app, "owner", "Faculty");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await browser.GetAsync($"/api/screen-cast/{session}/frame")).StatusCode);
        Assert.False(screens.IsActive(viewer, session));
        Assert.False(app.Services.GetRequiredService<AgentPresenceStore>().Snapshot()[UnitId].Connected);
    }

    [Fact]
    public async Task HumanJwtCannotConnectToDeviceHub()
    {
        await using var app = await StartServerAsync();
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("hub-tests", "humans",
            [new Claim(ClaimTypes.NameIdentifier, UnitId.ToString()), new Claim(ClaimTypes.Role, "Admin")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), "HS256"));
        await using var agent = CreateConnection(app, new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => agent.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    private sealed class HubRegistry : IUnitRegistryRepository
    {
        public volatile bool Active = true;
        public int Generation = 1;
        public Task<bool> IsAuthorizedAsync(Guid unit, int generation, Guid credential, CancellationToken ct) =>
            Task.FromResult(Active && unit == UnitId && credential == CredentialId && generation == Generation);
        public Task CleanupChallengesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<T> TransactionAsync<T>(RegistryLock key, Func<IUnitRegistryTransaction, Task<T>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegistryPage<RegistryUnit>> UnitsAsync(int? lab, string? search, int page, int size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task CachedHubPrincipalIsRevalidatedBeforePublishingWithoutAnotherHttpAuthentication()
    {
        await using var app = await StartServerAsync();
        var screens = app.Services.GetRequiredService<ScreenCastService>();
        var presence = app.Services.GetRequiredService<AgentPresenceStore>();
        var registry = (HubRegistry)app.Services.GetRequiredService<IUnitRegistryRepository>();
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(DeviceKey).Claims;
        var caller = new CachedCaller(new ClaimsPrincipal(new ClaimsIdentity(claims, "AgentBearer")));
        var hub = new AgentConnectionHub(NullLogger<AgentConnectionHub>.Instance, screens, presence, TimeProvider.System,
            app.Services.GetRequiredService<ApplicationScanCoordinator>(),
            app.Services.GetRequiredService<CLMS.Application.Monitoring.IApplicationRegistryRepository>()) { Context = caller };
        var filter = new AgentCredentialHubFilter(registry, screens, presence);
        await filter.OnConnectedAsync(new Microsoft.AspNetCore.SignalR.HubLifetimeContext(caller, app.Services, hub), _ => hub.OnConnectedAsync());
        var viewer = Viewer("owner");
        var session = await screens.StartAsync(viewer, UnitId);
        registry.Active = false;
        var invocation = new Microsoft.AspNetCore.SignalR.HubInvocationContext(caller, app.Services, hub,
            typeof(AgentConnectionHub).GetMethod(nameof(AgentConnectionHub.PublishScreenFrame))!, [Frame(session)]);
        var invoked = false;
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(async () =>
            await filter.InvokeMethodAsync(invocation, _ => { invoked = true; return ValueTask.FromResult<object?>(true); }));
        Assert.False(invoked);
        Assert.True(caller.Aborted);
        Assert.False(screens.IsActive(viewer, session));
        Assert.False(presence.Snapshot()[UnitId].Connected);
    }

    private sealed class CachedCaller : Microsoft.AspNetCore.SignalR.HubCallerContext
    {
        public CachedCaller(ClaimsPrincipal user)
        {
            User = user;
            Features.Set<Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature>(new HttpFeature());
        }
        public bool Aborted;
        public override string ConnectionId => "cached-websocket-connection";
        public override string? UserIdentifier => UnitId.ToString();
        public override ClaimsPrincipal User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }
    private sealed class HttpFeature : Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature
    {
        public HttpContext HttpContext { get; set; } = new DefaultHttpContext { Request = { Scheme = "https" } };
    }

    private static HubConnection CreateConnection(WebApplication app, string? key) => new HubConnectionBuilder()
        .WithUrl("https://localhost" + AgentConnectionProtocol.HubPath, options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult(key);
        })
        .Build();
}
