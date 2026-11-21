using System.Security.Claims;
using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Domain.Enums;
using CLMS.Web.Authentication;
using CLMS.Web.Components.Pages.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CLMS.Tests;

public sealed class PrivilegeComponentTests
{
    [Theory]
    [InlineData(Role.SuperAdmin, true)]
    [InlineData(Role.Admin, false)]
    public async Task UserPrivilegesDistinguishInheritanceOverridesAndEffectiveAccess(Role viewer, bool editable)
    {
        var target = PrivilegeAuthorizationTests.Account();
        var store = new TestAuthorizationStore(new EmailLoginTests.TestUsers(target));
        store.Overrides[target.Id] = new() { [Privileges.UnitsMonitor] = false, [Privileges.UsersView] = true };
        await using var services = Services(viewer, store).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await Render<UserPrivileges>(renderer, new() { [nameof(UserPrivileges.UserId)] = target.Id });
        Assert.Contains("Inherited", html); Assert.Contains("Effective", html);
        Assert.Contains("units.monitor", html); Assert.Contains("Revoke", html); Assert.Contains("Grant", html);
        Assert.Contains("Allowed", html); Assert.Contains("Denied", html);
        Assert.Equal(editable, html.Contains("<select"));
    }

    [Fact]
    public async Task SuperAdminAccountDisplaysAutomaticPrivilegesWithoutOverrideControls()
    {
        var target = PrivilegeAuthorizationTests.Account(Role.SuperAdmin);
        await using var services = Services(Role.SuperAdmin, new(new EmailLoginTests.TestUsers(target))).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await Render<UserPrivileges>(renderer, new() { [nameof(UserPrivileges.UserId)] = target.Id });
        Assert.Contains("Protected SuperAdmin account", html); Assert.Contains("Automatic", html);
        Assert.DoesNotContain("<select", html); Assert.DoesNotContain(">Denied<", html);
    }

    [Theory]
    [InlineData(Role.SuperAdmin, true)]
    [InlineData(Role.Admin, false)]
    public async Task RoleDefaultsAreLoadedOnlyForSuperAdminsEvenWhenRenderedDirectly(Role viewer, bool allowed)
    {
        var target = PrivilegeAuthorizationTests.Account();
        await using var services = Services(viewer, new(new EmailLoginTests.TestUsers(target))).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await Render<RolePrivileges>(renderer, new());
        Assert.Contains("SuperAdmin (protected)", html);
        Assert.Equal(!allowed, html.Contains("Only an active SuperAdmin can view role privileges."));
        Assert.Equal(allowed, html.Contains("type=\"checkbox\" checked"));
    }

    private static ServiceCollection Services(Role role, TestAuthorizationStore store)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddAuthorizationCore(PrivilegePolicies.Add);
        services.AddSingleton<IAuthorizationHandler, TestPrivilegeHandler>();
        services.AddSingleton<AuthenticationStateProvider>(new State(role));
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(PrivilegeAuthorizationTests.Account(role)));
        services.AddSingleton<IAuthorizationRepository>(store);
        services.AddSingleton<HumanAuthorizationCache>();
        services.AddSingleton<AuthorizationChanges>();
        services.AddSingleton<IAuthorizationChanges>(sp => sp.GetRequiredService<AuthorizationChanges>());
        services.AddScoped<PrivilegeManagementService>();
        return services;
    }

    private static async Task<string> Render<T>(HtmlRenderer renderer, Dictionary<string, object?> parameters) where T : IComponent
    {
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CascadingAuthenticationState>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = (RenderFragment)(builder =>
            { builder.OpenComponent<T>(0); foreach (var parameter in parameters) builder.AddAttribute(1, parameter.Key, parameter.Value); builder.CloseComponent(); }) })));
        return await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
    }

    private sealed class State(Role role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], "test"))));
    }
}
