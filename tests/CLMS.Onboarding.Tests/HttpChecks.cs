using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CLMS.Web.Authentication;
using CLMS.Web.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemes = CLMS.Web.Authentication.AuthenticationSchemes;

internal static class HttpChecks
{
    public static async Task<int> Run()
    {
        var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
        // Real cookie handlers, Razor components and antiforgery run locally. No test endpoint is part of the application.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AuthController).Assembly.GetName().Name });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(f.Accounts);
        builder.Services.AddControllersWithViews();
        builder.Services.AddRazorComponents();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(Schemes.Cookie)
            .AddCookie(Schemes.Cookie, o => { o.Cookie.Name = "test-main"; o.LoginPath = "/login"; })
            .AddCookie(Schemes.PasswordChange, o => { o.Cookie.Name = "test-password"; o.LoginPath = "/login"; })
            .AddCookie(Schemes.ExternalCookie, o => o.Cookie.Name = "test-google");
        builder.Services.AddAuthorization(o => o.AddPolicy("RegisterUsers", p => p.RequireRole("Admin")));
        builder.Services.AddRateLimiter(o => o.AddPolicy("GoogleLink", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery(); app.MapControllers();
        app.MapGet("/test/csrf", (HttpContext http, IAntiforgery anti) => anti.GetAndStoreTokens(http).RequestToken!);
        app.MapGet("/test/protected", () => "Protected resource").RequireAuthorization();
        app.MapGet("/test/google", async (HttpContext http) =>
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "google-sub"), new Claim(ClaimTypes.Email, user.Email),
                new Claim("google:email_verified", "true"), new Claim("google:hd", "usep.edu.ph") };
            await http.SignInAsync(Schemes.ExternalCookie, new ClaimsPrincipal(new ClaimsIdentity(claims, "google")),
                new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) });
            return Results.Ok();
        });
        await app.StartAsync();
        try
        {
            var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = Client(origin);
            var passed = 0;
            var csrf = await client.GetStringAsync("/test/csrf");
            using (var noCsrf = await client.PostAsync("/auth/cookie-login", Form("", ("Email", user.Email), ("Password", "initial-password"))))
                Check.That(noCsrf.StatusCode == HttpStatusCode.BadRequest);
            Pass("login rejects missing antiforgery token");
            using (var api = await client.PostAsJsonAsync("/api/auth/login", new { Email = user.Email, Password = "initial-password" }))
                Check.That(api.StatusCode == HttpStatusCode.Forbidden && (await api.Content.ReadAsStringAsync()).Contains("password_change_required"));
            Pass("JWT login returns password-change-required without access tokens");
            using (var login = await client.PostAsync("/auth/cookie-login", Form(csrf, ("Email", user.Email), ("Password", "initial-password"))))
                Check.That(login.Headers.Location?.OriginalString == "/account/change-password");
            Check.That((await client.GetAsync("/test/protected")).StatusCode == HttpStatusCode.Redirect);
            Pass("initial password creates restricted cookie that cannot authorize normal pages");
            var page = await client.GetStringAsync("/account/change-password"); csrf = Csrf(page);
            Check.That(page.Contains("must change your initial password") && page.Contains(user.Email));
            Check.That(page.Contains("<title>Change your password") && page.Contains("<main>") && page.Contains("</html>"));
            using (var mismatch = await client.PostAsync("/account/change-password", Form(csrf, ("CurrentPassword", "initial-password"), ("NewPassword", "new-password"), ("ConfirmPassword", "wrong"))))
            {
                var html = await mismatch.Content.ReadAsStringAsync();
                Check.That(mismatch.IsSuccessStatusCode && html.Contains("do not match") && !html.Contains("value=\"initial-password\""));
            }
            Pass("change-password component renders the layout, validates confirmation and never echoes passwords");
            using (var changed = await client.PostAsync("/account/change-password", Form(csrf, ("CurrentPassword", "initial-password"), ("NewPassword", "new-password"), ("ConfirmPassword", "new-password"))))
                Check.That(changed.Headers.Location?.OriginalString == "/login?message=password_changed" && user.PasswordChanged);
            Check.That((await client.GetAsync("/test/protected")).StatusCode == HttpStatusCode.Redirect);
            csrf = await client.GetStringAsync("/test/csrf");
            await client.PostAsync("/auth/cookie-login", Form(csrf, ("Email", user.Email), ("Password", "new-password")));
            Check.That((await client.GetAsync("/test/protected")).IsSuccessStatusCode);
            Pass("successful change clears restricted session and new password enables normal login");

            using var google = Client(origin); user.PasswordChanged = false;
            await google.GetAsync("/test/google");
            using (var callback = await google.GetAsync("/auth/google/callback"))
                Check.That(callback.Headers.Location?.OriginalString == "/auth/google/link");
            Check.That((await google.GetAsync("/test/protected")).StatusCode == HttpStatusCode.Redirect && f.Identities.Items.Count == 0);
            page = await google.GetStringAsync("/auth/google/link"); csrf = Csrf(page);
            Check.That(page.Contains("Link your Google account") && page.Contains(user.Email));
            Check.That(page.Contains("<title>Link your Google account") && page.Contains("<main>") && page.Contains("</html>"));
            Pass("unlinked Google callback shows password confirmation without signing in");
            using (var wrong = await google.PostAsync("/auth/google/link", Form(csrf, ("password", "wrong"))))
                Check.That((await wrong.Content.ReadAsStringAsync()).Contains("incorrect") && f.Identities.Items.Count == 0);
            using (var linked = await google.PostAsync("/auth/google/link", Form(csrf, ("password", "new-password"), ("userId", Guid.NewGuid().ToString()), ("email", "attacker@usep.edu.ph"))))
                Check.That(linked.Headers.Location?.OriginalString == "/account/change-password");
            Check.That(f.Identities.Items.Single().UserId == user.Id && (await google.GetAsync("/test/protected")).StatusCode == HttpStatusCode.Redirect);
            Pass("Google link uses protected identity, checks password, and enforces first-password change");
            user.PasswordChanged = true;
            using var knownGoogle = Client(origin); await knownGoogle.GetAsync("/test/google");
            using (var callback = await knownGoogle.GetAsync("/auth/google/callback")) Check.That(callback.Headers.Location?.OriginalString == "/");
            Check.That((await knownGoogle.GetAsync("/test/protected")).IsSuccessStatusCode);
            Pass("linked Google subject signs in normally after password change");
            using var noTicket = Client(origin);
            using (var link = await noTicket.GetAsync("/auth/google/link")) Check.That(link.Headers.Location?.OriginalString == "/login?error=google_verification_failed");
            Pass("link page rejects absent or expired external ticket");

            f.Identities.Items.Clear();
            using var cancelGoogle = Client(origin);
            await cancelGoogle.GetAsync("/test/google"); await cancelGoogle.GetAsync("/auth/google/callback");
            page = await cancelGoogle.GetStringAsync("/auth/google/link");
            using (var cancelled = await cancelGoogle.PostAsync("/auth/google/link/cancel", Form(Csrf(page))))
                Check.That(cancelled.Headers.Location?.OriginalString == "/login");
            using (var afterCancel = await cancelGoogle.GetAsync("/auth/google/link"))
                Check.That(afterCancel.Headers.Location?.OriginalString == "/login?error=google_verification_failed");
            Check.That(f.Identities.Items.Count == 0);
            Pass("Google component cancellation clears the external ticket without linking");

            user.PasswordChanged = false;
            using var cancelPassword = Client(origin);
            csrf = await cancelPassword.GetStringAsync("/test/csrf");
            await cancelPassword.PostAsync("/auth/cookie-login", Form(csrf, ("Email", user.Email), ("Password", "new-password")));
            page = await cancelPassword.GetStringAsync("/account/change-password");
            using (var cancelled = await cancelPassword.PostAsync("/account/change-password/cancel", Form(Csrf(page))))
                Check.That(cancelled.Headers.Location?.OriginalString == "/login");
            Check.That((await cancelPassword.GetAsync("/test/protected")).StatusCode == HttpStatusCode.Redirect && !user.PasswordChanged);
            Pass("password component cancellation clears the restricted session");
            return passed;
            void Pass(string name) { passed++; Console.WriteLine("PASS HTTP " + name); }
        }
        finally { await app.StopAsync(); }
    }
    private static HttpClient Client(string origin) => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(origin) };
    private static string Csrf(string html)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Check.That(token.Length > 0); return WebUtility.HtmlDecode(token);
    }
    private static FormUrlEncodedContent Form(string csrf, params (string Name, string Value)[] values) =>
        new(values.Append(("__RequestVerificationToken", csrf)).Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)));
}
