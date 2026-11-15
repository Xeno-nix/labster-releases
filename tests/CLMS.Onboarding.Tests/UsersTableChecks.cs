using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CLMS.Application.Identity.Services;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Web.Components.Pages.Admin;
using CLMS.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class UsersTableChecks
{
    public static async Task<int> Run()
    {
        var fixture = new Fixture();
        var actor = Fixture.User("current-admin@example.com", true);
        actor.UserRole = Role.Admin;
        fixture.Users.Items[actor.Id] = actor;
        // Enough rows to exercise the last page and a bounded page-number window.
        for (var i = 1; i <= 62; i++)
        {
            var user = Fixture.User($"person{i:000}@example.com", true);
            user.FirstName = new Name("Person");
            user.LastName = new Name($"User{(char)('A' + (i - 1) / 26)}{(char)('A' + (i - 1) % 26)}");
            user.UserRole = i % 3 == 0 ? Role.Admin : Role.Faculty;
            fixture.Users.Items[user.Id] = user;
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString()), new Claim(ClaimTypes.Role, "Admin")], "test"));
        var activator = new RecordingActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<AuthenticationStateProvider>(new FixedState(principal));
        services.AddSingleton(new UserManagementService(fixture.Users));
        services.AddSingleton(fixture.Accounts);
        services.AddSingleton<ToastService>();
        services.AddSingleton<IComponentActivator>(activator);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new TestRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent(typeof(Users), ParameterView.Empty));
        await root.QuiescenceTask;
        var passed = 0;

        await Verify("initial page limits rows, excludes the actor and shows pagination", html =>
        {
            Check.That(Rows(html) == 10 && html.Contains("Showing 1–10 of 62 users"));
            Check.That(!html.Contains(actor.Email) && html.Contains("person001@example.com") && !html.Contains("person011@example.com"));
            Check.That(html.Contains("Users table pagination") && Regex.Matches(html, "Go to page ").Count == 4);
        });
        await Change(() => Invoke("ChangePage", 7));
        await Verify("last page contains only the remaining rows", html =>
            Check.That(Rows(html) == 2 && html.Contains("Showing 61–62 of 62 users") && !Regex.IsMatch(html, @">\s*Next\s*</button>")));

        await Change(() => Set("Search", "  PERSON012@EXAMPLE.COM  "));
        await Verify("email search ignores case and surrounding whitespace and resets the page", html =>
            Check.That(Rows(html) == 1 && html.Contains("person012@example.com") && html.Contains("Showing 1–1 of 1 user")));
        await Change(() => Set("RoleFilter", "Faculty"));
        await Verify("search and role filters combine and show a useful empty state", html =>
            Check.That(Rows(html) == 0 && html.Contains("No users match your search or role filter.") && html.Contains("Showing 0–0 of 0 users")));
        await Change(() => Invoke("ClearFilters"));
        await Change(() => { Invoke("ChangePage", 3); Set("RoleFilter", "Admin"); });
        await Verify("role filtering resets the page and counts only matching users", html =>
            Check.That(Rows(html) == 10 && html.Contains("Showing 1–10 of 20 users") && html.Contains("(62 total)") && !html.Contains("person001@example.com")));

        await Change(() => { Invoke("ClearFilters"); Set("Search", "pErSoN uSeRaL"); });
        await Verify("name search ignores case", html => Check.That(Rows(html) == 1 && html.Contains("person012@example.com")));
        await Change(() => { Invoke("ClearFilters"); Invoke("ChangePage", 4); Set("PageSize", 25); });
        await Verify("changing page size resets the page and changes the row limit", html =>
            Check.That(Rows(html) == 25 && html.Contains("Showing 1–25 of 62 users")));
        await Change(() => Invoke("ChangePage", 3));
        await Verify("page ranges remain correct with a larger page size", html =>
            Check.That(Rows(html) == 12 && html.Contains("Showing 51–62 of 62 users")));

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            fixture.Users.Items.Clear();
            await (Task)Method("LoadUsersAsync").Invoke(activator.Page, null)!;
            Rerender();
        });
        await Verify("a shrinking dataset clamps pagination and retains the no-users message", html =>
            Check.That(Rows(html) == 0 && html.Contains("No registered users to display.") && html.Contains("Showing 0–0 of 0 users") && !html.Contains("Users table pagination")));
        return passed;

        static int Rows(string html) => Regex.Matches(html, "aria-label=\"View Person").Count;
        static MethodInfo Method(string name) => typeof(Users).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Invoke(string name, params object[] arguments) => Method(name).Invoke(activator.Page, arguments);
        void Set(string name, object value) => typeof(Users).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(activator.Page, value);
        void Rerender() => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, null);
        Task Change(Action action) => renderer.Dispatcher.InvokeAsync(() => { action(); Rerender(); });
        async Task Verify(string name, Action<string> check)
        {
            var html = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
            check(html);
            passed++;
            Console.WriteLine("PASS Users " + name);
        }
    }

    private sealed class RecordingActivator : IComponentActivator
    {
        public Users Page { get; private set; } = null!;
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is Users page) Page = page;
            return component;
        }
    }

    // Render the interactive page locally without creating a server circuit.
    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
            IComponentActivator componentActivator, IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/users");
    }
}
