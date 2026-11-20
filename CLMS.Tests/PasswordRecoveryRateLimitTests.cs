using System.Net;
using CLMS.Infrastructure.Auth;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CLMS.Tests;

public sealed class PasswordRecoveryRateLimitTests
{
    [Fact]
    public void CooldownAndRollingWindowReturnTheActualNextPermittedTime()
    {
        var clock = new EmailLoginTests.TestClock(); var queue = new PasswordRecoveryQueue(clock);
        var start = clock.GetUtcNow(); var first = queue.Enqueue("person@example.com");
        Assert.False(first.RateLimited); Assert.Equal(start.AddSeconds(60), first.RetryAt);
        clock.Advance(TimeSpan.FromSeconds(30));
        var blocked = queue.Enqueue("person@example.com"); Assert.True(blocked.RateLimited); Assert.Equal(first.RetryAt, blocked.RetryAt);
        clock.Advance(TimeSpan.FromSeconds(30));
        for (var i = 1; i < 5; i++)
        {
            Assert.False(queue.Enqueue("person@example.com").RateLimited);
            if (i < 4) clock.Advance(TimeSpan.FromMinutes(1));
        }
        var fifthWait = queue.Enqueue("person@example.com");
        Assert.True(fifthWait.RateLimited); Assert.Equal(start.AddMinutes(15), fifthWait.RetryAt);
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False(queue.Enqueue("person@example.com").RateLimited); // Exactly the first request's 15-minute boundary.
    }

    [Fact]
    public async Task ConcurrentRequestsCannotBypassTheCooldown()
    {
        var clock = new EmailLoginTests.TestClock(); var queue = new PasswordRecoveryQueue(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => queue.Enqueue("person@example.com"))));
        Assert.Single(results, r => !r.RateLimited);
        Assert.All(results, r => Assert.Equal(clock.GetUtcNow().AddMinutes(1), r.RetryAt));
        // Registration/existence is not an input to either rate calculation.
        var unknown = queue.Enqueue("unknown@example.com"); Assert.False(unknown.RateLimited);
        Assert.Equal(results[0].RetryAt, unknown.RetryAt);
    }

    [Fact]
    public async Task IpRejectionRendersTheFormWithAccurateRetryTimingAndNoStoreHeaders()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews(); builder.Services.AddRazorComponents();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("PasswordRecovery", PasswordRecoveryRateLimit.ForClient);
            options.OnRejected = PasswordRecoveryRateLimit.OnRejectedAsync;
        });
        await using var app = builder.Build();
        app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-IP"].FirstOrDefault() ?? "127.0.0.1"); return next(context); });
        app.UseRateLimiter(); app.UseAntiforgery();
        app.MapPost("/forgot-password", () => "accepted").RequireRateLimiting("PasswordRecovery");
        await app.StartAsync(); using var client = app.GetTestClient(); client.BaseAddress = new("https://localhost");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "person" + i + "@example.com" }))).StatusCode);
        var rejected = await client.PostAsync("/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "person@example.com" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 60.01, 900);
        Assert.True(rejected.Headers.CacheControl!.NoStore);
        var html = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("Send in ", html); Assert.Contains("data-recovery-cooldown=\"", html);
        Assert.Contains("cooldown-clock", html); Assert.Contains("disabled", html);
        Assert.Contains("value=\"person@example.com\"", html);
        client.DefaultRequestHeaders.Add("X-Test-IP", "127.0.0.2");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "another@example.com" }))).StatusCode);
    }
}
