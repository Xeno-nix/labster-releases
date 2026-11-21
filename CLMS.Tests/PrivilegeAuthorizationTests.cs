using System.Security.Claims;
using System.Text.Encodings.Web;
using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Auth;
using CLMS.Web.Authentication;
using CLMS.Web.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using User = CLMS.Domain.Entities.Identity.User;

namespace CLMS.Tests;

public sealed class PrivilegeAuthorizationTests
{
    [Fact]
    public void EffectiveAccessAppliesGrantsRevocationsAndResetWithoutChangingRoles()
    {
        var user = new AuthorizationSnapshot(Guid.NewGuid(), Role.Faculty, true,
            new HashSet<string> { Privileges.UnitsView, Privileges.UnitsMonitor },
            new Dictionary<string, bool> { [Privileges.UsersView] = true, [Privileges.UnitsMonitor] = false, ["unknown"] = true });
        Assert.Contains(Privileges.UnitsView, user.Effective); Assert.Contains(Privileges.UsersView, user.Effective);
        Assert.DoesNotContain(Privileges.UnitsMonitor, user.Effective); Assert.DoesNotContain("unknown", user.Effective);
        Assert.Contains(Privileges.UnitsMonitor, (user with { Overrides = new Dictionary<string, bool>() }).Effective);
        Assert.Empty((user with { IsActive = false }).Effective);
        Assert.Empty((user with { Role = Role.Student }).Effective);
    }

    [Fact]
    public void SuperAdminIsAutomaticButNeverBypassesAccountValidity()
    {
        var user = new AuthorizationSnapshot(Guid.NewGuid(), Role.SuperAdmin, true, new HashSet<string>(),
            Privileges.All.ToDictionary(p => p.Id, _ => false));
        Assert.Equal(Privileges.All.Count, user.Effective.Count);
        Assert.Contains(Privileges.SecurityManage, user.Effective);
        Assert.DoesNotContain("unknown", user.Effective);
        Assert.Empty((user with { IsActive = false }).Effective);
        Assert.DoesNotContain(Privileges.SecurityManage, (user with { Role = Role.Admin, Overrides = new Dictionary<string, bool> { [Privileges.SecurityManage] = true } }).Effective);
    }

    [Fact]
    public async Task LiveResolutionObservesRoleDefaultsAndOverridesImmediately()
    {
        var user = Account(); var users = new EmailLoginTests.TestUsers(user); var store = new TestAuthorizationStore(users);
        Assert.True(await store.HasPrivilegeAsync(user.Id, Privileges.UnitsMonitor));
        store.Roles[Role.Faculty].Remove(Privileges.UnitsMonitor);
        Assert.False(await store.HasPrivilegeAsync(user.Id, Privileges.UnitsMonitor));
        store.Overrides[user.Id] = new() { [Privileges.UnitsMonitor] = true };
        Assert.True(await store.HasPrivilegeAsync(user.Id, Privileges.UnitsMonitor));
        store.Overrides[user.Id][Privileges.UnitsMonitor] = false;
        Assert.False(await store.HasPrivilegeAsync(user.Id, Privileges.UnitsMonitor));
        Assert.False(await store.HasPrivilegeAsync(Guid.NewGuid(), Privileges.UnitsView));
        Assert.False(await store.HasPrivilegeAsync(user.Id, "unknown"));
    }

    [Fact]
    public async Task PoliciesUseTheCurrentUserRecordRatherThanAClaimedSuperAdminRole()
    {
        var user = Account(); var users = new EmailLoginTests.TestUsers(user); var store = new TestAuthorizationStore(users); var crypto = Crypto();
        var principal = Principal(user, crypto, "SuperAdmin");
        var actor = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }, new FixedState(principal), users, crypto, store);
        var handler = new PrivilegeAuthorizationHandler(actor, store);
        var requirement = new PrivilegeRequirement(Privileges.SecurityManage);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await handler.HandleAsync(context); Assert.False(context.HasSucceeded); Assert.Equal(1, store.Denials);
        user.UserRole = Role.SuperAdmin;
        principal = Principal(user, crypto, "SuperAdmin");
        context = new([requirement], principal, null); await handler.HandleAsync(context); Assert.True(context.HasSucceeded);
        user.LockoutEnd = DateTime.UtcNow.AddMinutes(1);
        context = new([requirement], principal, null); await handler.HandleAsync(context); Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AuthorizationChangesReevaluateExistingCircuitsAndInvalidateChangedAccounts()
    {
        var user = Account(); var crypto = Crypto(); var users = new EmailLoginTests.TestUsers(user);
        var services = new ServiceCollection(); services.AddSingleton<IUserRepository>(users);
        services.AddSingleton<IAccountSecurityCrypto>(crypto); services.AddScoped<HumanSessionValidator>();
        await using var container = services.BuildServiceProvider();
        var changes = new AuthorizationChanges(Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthorizationChanges>.Instance);
        using var state = new HumanRevalidatingStateProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            container.GetRequiredService<IServiceScopeFactory>(), changes);
        state.SetAuthenticationState(Task.FromResult(new AuthenticationState(Principal(user, crypto, "Faculty"))));
        var notification = new TaskCompletionSource<Task<AuthenticationState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.AuthenticationStateChanged += task => notification.TrySetResult(task);
        changes.NotifyChanged();
        Assert.True((await (await notification.Task.WaitAsync(TimeSpan.FromSeconds(5)))).User.Identity!.IsAuthenticated);
        notification = new(TaskCreationOptions.RunContinuationsAsynchronously);
        user.LockoutEnd = DateTime.UtcNow.AddMinutes(5);
        changes.NotifyChanged();
        Assert.False((await (await notification.Task.WaitAsync(TimeSpan.FromSeconds(5)))).User.Identity?.IsAuthenticated == true);
        Assert.False((await state.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task DirectHttpCallsCannotEscalateAndRootMutationsRequireAntiforgery()
    {
        var user = Account(); var users = new EmailLoginTests.TestUsers(user); var store = new TestAuthorizationStore(users);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(user); builder.Services.AddSingleton<IUserRepository>(users);
        builder.Services.AddSingleton<IAuthorizationRepository>(store); builder.Services.AddSingleton<IPrivilegeResolver>(store);
        builder.Services.AddSingleton<IAccountSecurityCrypto>(Crypto()); builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<AuthenticationStateProvider>(new FixedState(new()));
        builder.Services.AddScoped<CurrentUser>(); builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        builder.Services.AddScoped<IAuthorizationHandler, PrivilegeAuthorizationHandler>();
        builder.Services.AddSingleton<IAuthorizationChanges, TestAuthorizationChanges>(); builder.Services.AddScoped<PrivilegeManagementService>();
        builder.Services.AddAuthorization(PrivilegePolicies.Add);
        builder.Services.AddAuthentication(CLMS.Web.Authentication.AuthenticationSchemes.Cookie)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(CLMS.Web.Authentication.AuthenticationSchemes.Cookie, _ => { });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PrivilegeManagementController).Assembly); builder.Services.AddAntiforgery();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery(); app.MapControllers();
        app.MapGet("/test-token", (HttpContext http, IAntiforgery antiforgery) => Results.Ok(antiforgery.GetAndStoreTokens(http).RequestToken));
        await app.StartAsync(); using var client = app.GetTestClient(); client.BaseAddress = new Uri("https://localhost");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/authorization/roles")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true"); client.DefaultRequestHeaders.Add("X-Test-Role", "SuperAdmin");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/authorization/roles")).StatusCode);
        store.Overrides[user.Id] = new() { [Privileges.UsersManagePrivileges] = true, [Privileges.UsersView] = true };
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PutAsync($"/api/admin/authorization/users/{user.Id}/overrides", Json("{\"privilege\":\"security.manage\",\"isGranted\":true}"))).StatusCode);
        var details = await client.GetStringAsync($"/api/admin/authorization/users/{user.Id}/privileges");
        Assert.DoesNotContain(user.PasswordHash, details);
        user.UserRole = Role.SuperAdmin;
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/admin/authorization/roles")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/authorization/roles/Faculty/privileges", Json("{\"privileges\":[\"units.view\"]}"))).StatusCode);
        var token = await client.GetAsync("/test-token");
        client.DefaultRequestHeaders.Add("Cookie", token.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", System.Text.Json.JsonSerializer.Deserialize<string>(await token.Content.ReadAsStringAsync()));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/authorization/roles/Faculty/privileges", Json("{\"privileges\":[\"unknown\"]}"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await client.PutAsync("/api/admin/authorization/roles/Faculty/privileges", Json("{\"privileges\":[\"units.view\"]}"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PutAsync($"/api/admin/authorization/users/{user.Id}/overrides", Json("{\"privilege\":\"units.view\",\"isGranted\":false}"))).StatusCode);
    }

    private static StringContent Json(string value) => new(value, System.Text.Encoding.UTF8, "application/json");
    internal static User Account(Role role = Role.Faculty) => new(Guid.NewGuid(), new Name("Test"), new Name("User"), "user@example.test", role, "not-a-real-password-hash");
    private static AccountSecurityCrypto Crypto() => new(Options.Create(new JwtOptions { Key = new string('k', 64) }));
    private static ClaimsPrincipal Principal(User user, IAccountSecurityCrypto crypto, string role) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, role), new Claim("principal_type", "human"), new Claim("security_stamp", crypto.Stamp(user))], CookieAuthenticationDefaults.AuthenticationScheme));
    private sealed class FixedState(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }
    private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        User user, IAccountSecurityCrypto crypto) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("X-Test-Authenticated")
            ? AuthenticateResult.Success(new AuthenticationTicket(Principal(user, crypto, Request.Headers["X-Test-Role"].ToString()), Scheme.Name)) : AuthenticateResult.NoResult());
    }
}
