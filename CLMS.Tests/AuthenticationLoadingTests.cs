using System.Reflection;
using CLMS.Application.Authorization;
using CLMS.Web.Authentication;
using CLMS.Web.Components.Layout;
using CLMS.Web.Components.Shared;
using CLMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CLMS.Tests;

public sealed class AuthenticationLoadingTests
{
    [Theory]
    [InlineData(typeof(PrivilegedPage))]
    [InlineData(typeof(AuthenticatedPage))]
    public async Task PendingAuthorizationShowsOnlyLoadingThenRevealsTheCompleteShell(Type page)
    {
        await using var fixture = Fixture();
        var state = fixture.Services.GetRequiredService<AuthenticationStateProvider>();
        ((HumanRevalidatingStateProvider)state).SetAuthenticationState(Task.FromResult(new AuthenticationState(fixture.Principal)));
        var pending = new TaskCompletionSource<AuthorizationSnapshot?>();
        fixture.Store.ReadSnapshot = (_, _) => pending.Task;
        await using var renderer = new HtmlRenderer(fixture.Services, fixture.Services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<CascadingAuthenticationState>(Route(page)));
        var loading = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("role=\"status\"", loading);
        Assert.DoesNotContain("main-sidebar", loading);
        Assert.DoesNotContain("toast-container", loading);
        Assert.DoesNotContain("Authorized content", loading);
        pending.SetResult(fixture.Snapshot());
        await root.QuiescenceTask;
        var ready = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("main-sidebar", ready);
        Assert.Contains("Authorized content", ready);
        Assert.DoesNotContain("authentication-loading", ready);
        Assert.Equal(1, fixture.Store.SnapshotReads);

        var warm = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<CascadingAuthenticationState>(Route(page)));
        var warmHtml = await renderer.Dispatcher.InvokeAsync(warm.ToHtmlString);
        Assert.Contains("Authorized content", warmHtml);
        Assert.DoesNotContain("authentication-loading", warmHtml);
        Assert.Equal(1, fixture.Store.SnapshotReads);
    }

    [Fact]
    public async Task AdminRevocationRemovesTheCurrentPageAndSidebarLinkWithoutSigningOut()
    {
        await using var fixture = Fixture();
        var state = (HumanRevalidatingStateProvider)fixture.Services.GetRequiredService<AuthenticationStateProvider>();
        state.SetAuthenticationState(Task.FromResult(new AuthenticationState(fixture.Principal)));
        await using var renderer = new HtmlRenderer(fixture.Services, fixture.Services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CascadingAuthenticationState>(Route(typeof(PrivilegedPage))));
        Assert.Contains("Authorized content", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            fixture.Store.Roles[fixture.User.UserRole].Remove(Privileges.UnitsView);
            fixture.Changes.NotifyChanged();
        });
        var denied = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Access Denied", denied);
        Assert.DoesNotContain("Authorized content", denied);
        Assert.DoesNotContain("href=\"/unit-monitoring\"", denied);
        Assert.DoesNotContain("authentication-loading", denied);
        Assert.True((await state.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal(2, fixture.Store.SnapshotReads);
    }

    [Fact]
    public async Task FailedShellPreparationEndsLoadingAndDisplaysAnError()
    {
        await using var fixture = Fixture();
        var state = (HumanRevalidatingStateProvider)fixture.Services.GetRequiredService<AuthenticationStateProvider>();
        state.SetAuthenticationState(Task.FromResult(new AuthenticationState(fixture.Principal)));
        var pending = new TaskCompletionSource<AuthorizationSnapshot?>();
        fixture.Store.ReadSnapshot = (_, _) => pending.Task;
        await using var renderer = new HtmlRenderer(fixture.Services, fixture.Services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<CascadingAuthenticationState>(Route(typeof(AuthenticatedPage))));
        pending.SetException(new InvalidOperationException("Database unavailable"));
        await root.QuiescenceTask;
        var error = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Unable to verify access", error);
        Assert.DoesNotContain("authentication-loading", error);
        Assert.DoesNotContain("main-sidebar", error);
        Assert.DoesNotContain("Authorized content", error);
    }

    [Theory]
    [InlineData("CLMS.Web.Components.Pages.Home", typeof(MainLayout))]
    [InlineData("CLMS.Web.Components.Pages.ClassManagement", typeof(MainLayout))]
    [InlineData("CLMS.Web.Components.Pages.Account.Settings", typeof(MainLayout))]
    [InlineData("CLMS.Web.Components.Pages.Account.SecurityForm", typeof(StandaloneLayout))]
    [InlineData("CLMS.Web.Components.Pages.Account.ChangePassword", typeof(StandaloneLayout))]
    [InlineData("CLMS.Web.Components.Pages.Account.GoogleLink", typeof(StandaloneLayout))]
    [InlineData("CLMS.Web.Components.Pages.Account.TwoFactorConfirmation", typeof(StandaloneLayout))]
    [InlineData("CLMS.Web.Components.Pages.Login", typeof(PublicLayout))]
    [InlineData("CLMS.Web.Components.Pages.LoggingTerminal", typeof(PublicLayout))]
    [InlineData("CLMS.Web.Components.Pages.Inventory.InventoryPrint", typeof(PrintLayout))]
    [InlineData("CLMS.Web.Components.Pages.Monitoring.AllScreens", typeof(ScreenWallLayout))]
    public void PageLayoutDefaultsPreserveExplicitOverrides(string pageName, Type layout)
    {
        var page = typeof(MainLayout).Assembly.GetType(pageName, throwOnError: true)!;
        Assert.Equal(layout, page.GetCustomAttribute<LayoutAttribute>()?.LayoutType);
    }

    private static HumanAuthorizationCacheTests.Fixture Fixture() => new(services =>
    {
        services.AddAuthorizationCore(PrivilegePolicies.Add);
        services.AddSingleton<IAuthorizationHandler, PrivilegeAuthorizationHandler>();
        services.AddSingleton<AuthenticationStateProvider, HumanRevalidatingStateProvider>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<ToastService>(); services.AddSingleton<DialogService>();
    });

    private static ParameterView Route(Type page) => ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(CascadingAuthenticationState.ChildContent)] = (RenderFragment)(builder =>
        {
            builder.OpenComponent<AuthorizeRouteView>(0);
            builder.AddAttribute(1, nameof(AuthorizeRouteView.RouteData), new RouteData(page, new Dictionary<string, object?>()));
            builder.AddAttribute(2, nameof(AuthorizeRouteView.Authorizing), (RenderFragment)(loading =>
            { loading.OpenComponent<AuthenticationLoading>(0); loading.CloseComponent(); }));
            builder.AddAttribute(3, nameof(AuthorizeRouteView.NotAuthorized), (RenderFragment<AuthenticationState>)(_ => denied =>
            {
                denied.OpenComponent<LayoutView>(0);
                denied.AddAttribute(1, nameof(LayoutView.Layout), typeof(MainLayout));
                denied.AddAttribute(2, nameof(LayoutView.ChildContent), (RenderFragment)(body => body.AddContent(0, "Access Denied")));
                denied.CloseComponent();
            }));
            builder.CloseComponent();
        })
    });

    [Layout(typeof(MainLayout))]
    [Authorize(Policy = Privileges.Prefix + Privileges.UnitsView)]
    public sealed class PrivilegedPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "Authorized content");
    }

    [Layout(typeof(MainLayout))]
    [Authorize]
    public sealed class AuthenticatedPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "Authorized content");
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}
