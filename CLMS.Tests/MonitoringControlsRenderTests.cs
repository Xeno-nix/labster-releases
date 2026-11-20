using System.Security.Claims;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Web.Components.Pages.Monitoring;
using CLMS.Web.Services;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace CLMS.Tests;

public sealed class MonitoringControlsRenderTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Faculty", false)]
    [InlineData(null, false)]
    public async Task CommandMenuUsesRealAuthorizationComponents(string? role, bool visible)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AuthenticationStateProvider>(new RenderAuthentication(role));
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<DialogService>();
        // Static rendering must never execute workstation commands or contact a database.
        services.AddSingleton(new WorkstationCommandService(null!, null!, null!, null!, TimeProvider.System,
            NullLogger<WorkstationCommandService>.Instance));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<QuickControls>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuickControls.Units)] = new List<UnitMonitoringDTO>
                {
                    new() { Id = Guid.NewGuid(), Name = "PC 01", IsOnline = true, LastHeartbeatUtc = DateTime.UtcNow }
                },
                [nameof(QuickControls.Enabled)] = true,
                [nameof(QuickControls.ObservedAtUtc)] = DateTime.UtcNow
            }));
            return output.ToHtmlString();
        });
        Assert.Equal(visible, html.Contains("commands-trigger"));
        Assert.DoesNotContain("<authorizeview", html.ToLowerInvariant());
    }

    [Fact]
    public async Task UnitDetailsPassesTheRegisteredUnitNameToItsPreview()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AuthenticationStateProvider>(new RenderAuthentication("Admin"));
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<DialogService>();
        services.AddSingleton(new WorkstationCommandService(null!, null!, null!, null!, TimeProvider.System,
            NullLogger<WorkstationCommandService>.Instance));
        services.AddSingleton(new CLMS.Web.Services.ScreenCasting.ScreenCastService(null!, TimeProvider.System,
            NullLogger<CLMS.Web.Services.ScreenCasting.ScreenCastService>.Instance));
        services.AddSingleton(provider => new CLMS.Web.Services.ScreenCasting.LabScreenCastService(
            provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<CLMS.Web.Services.ScreenCasting.ScreenCastService>(),
            TimeProvider.System, NullLogger<CLMS.Web.Services.ScreenCasting.LabScreenCastService>.Instance));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<UnitDetailsModal>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(UnitDetailsModal.Unit)] = new UnitMonitoringDTO { Id = Guid.NewGuid(), Name = "Lab PC 07", Number = 7, Laboratory = "Lab 1" },
                [nameof(UnitDetailsModal.ObservedAtUtc)] = DateTime.UtcNow
            }));
            return output.ToHtmlString();
        });
        Assert.Contains("alt=\"Live screen of Lab PC 07\"", html);
        Assert.DoesNotContain("Live screen of Unit.Name", html);
    }

    private sealed class RenderAuthentication(string? role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(role is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"))));
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Static render cannot invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
