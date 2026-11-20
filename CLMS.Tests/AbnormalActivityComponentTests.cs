using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Web.Components.Pages.Monitoring;
using CLMS.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace CLMS.Tests;

public sealed class AbnormalActivityComponentTests
{
    [Fact]
    public async Task ActiveViewUpdatesOverItsExistingCircuitWhenAnActivityEnds()
    {
        var id = Guid.NewGuid(); var activity = new AbnormalActivity(Guid.NewGuid(), id, AbnormalActivityType.FlaggedApplication,
            Guid.NewGuid(), "Review application", DateTime.UtcNow, null);
        var repository = new ActivityTestRepository(new ApplicationMonitoringTests.Registry()); repository.Items.Add(activity);
        await using var provider = Services(repository, new ActivityTestUnits(id), "Faculty").BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CascadingAuthenticationState>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = (RenderFragment)(b => { b.OpenComponent<AbnormalActivities>(0); b.CloseComponent(); }) })));
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Abnormal Activity Monitoring", html); Assert.Contains("Review application", html);
        Assert.Contains("Flagged Application", html); Assert.Contains("bg-amber-", html); Assert.DoesNotContain("Ended At</th>", html);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            repository.Items[0] = activity with { EndedAt = DateTime.UtcNow };
            provider.GetRequiredService<MonitoringUpdates>().NotifyChanged();
        });
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("No active activities match", html); Assert.DoesNotContain("Review application", html);
    }

    [Fact]
    public async Task UnauthorizedViewerCannotRetrieveActivityRowsEvenIfTheComponentIsRenderedDirectly()
    {
        var repository = new ActivityTestRepository(new ApplicationMonitoringTests.Registry());
        await using var provider = Services(repository, new ActivityTestUnits(Guid.NewGuid()), "Student").BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CascadingAuthenticationState>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = (RenderFragment)(b => { b.OpenComponent<AbnormalActivities>(0); b.CloseComponent(); }) })));
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("You do not have access", html); Assert.Equal(0, repository.Queries);
    }

    private static ServiceCollection Services(ActivityTestRepository repository, ActivityTestUnits units, string role)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddAuthorizationCore();
        services.AddSingleton<AuthenticationStateProvider>(new State(role));
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton<IUnitMonitoringRepository>(units);
        services.AddSingleton<IAbnormalActivityRepository>(repository);
        services.AddSingleton<MonitoringUpdates>();
        services.AddSingleton<IAbnormalActivityNotifications>(sp => sp.GetRequiredService<MonitoringUpdates>());
        services.AddScoped<AbnormalActivityService>();
        return services;
    }
    private sealed class State(string role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(AbnormalActivityTests.Viewer(role)));
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Static rendering must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
