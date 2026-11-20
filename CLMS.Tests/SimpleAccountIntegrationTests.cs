using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Identity.Services;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Auth;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using AuthenticationService = CLMS.Application.Identity.Services.AuthenticationService;
using AuthenticationSchemes = CLMS.Web.Authentication.AuthenticationSchemes;

namespace CLMS.Tests;

/// <summary>Uses the unchanged user/refresh schema in a uniquely named disposable database, with no real email delivery.</summary>
public sealed class SimpleAccountIntegrationTests : IAsyncLifetime
{
    private MySqlConnection? admin;
    private string database = "";
    private MySqlConnectionFactory connections = null!;
    private UserRepository users = null!;
    private ResetTokenRepository resets = null!;
    private RefreshTokenRepository refresh = null!;
    private EmailLoginService login = null!;
    private PasswordRecoveryService recovery = null!;
    private AuthenticationService authentication = null!;
    private TwoFactorSettingsService settings = null!;
    private readonly PasswordHasher passwords = new();
    private readonly EmailLoginTests.TestEmail email = new();
    private readonly AccountSecurityCrypto crypto = new(Options.Create(new JwtOptions { Key = new string('K', 64) }));
    private readonly PasswordRecoveryQueue queue = new(TimeProvider.System);
    private readonly AccountOptions options = new() { PublicUrl = "https://labster.example", SupportEmail = "support@example.com", AllowedWorkspaceDomains = ["example.com"] };
    private User user = null!;

    public async Task InitializeAsync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "CLMS.slnx"))) dir = dir.Parent!;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(dir.FullName, "CLMS.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).Build();
        var cs = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL") ?? config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Configure CLMS_TEST_MYSQL."))
            { Database = "", AllowUserVariables = true, ConnectionTimeout = 5 };
        admin = new(cs.ConnectionString); await admin.OpenAsync();
        database = "clms_account_test_" + Guid.NewGuid().ToString("N");
        await admin.ExecuteAsync($"CREATE DATABASE `{database}`"); cs.Database = database;
        connections = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = cs.ConnectionString }).Build());
        await using var c = connections.CreateConnection();
        foreach (var table in new[] { "users", "refresh_tokens", "external_identities" })
            await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(dir.FullName, "Database/clms/" + table + ".sql")));
        users = new(connections); resets = new(connections); refresh = new(connections);
        var challenges = new EmailLoginChallenges(crypto, TimeProvider.System);
        login = new(challenges, users, crypto, email);
        settings = new(users, challenges, crypto, passwords, email);
        recovery = new(users, resets, crypto, passwords, queue, email, options, TimeProvider.System);
        authentication = new(users, passwords, new JwtAuthenticationService(Options.Create(new JwtOptions
            { Issuer = "tests", Audience = "human", Key = new string('K', 64), SigningAlgorithm = "HS256", AccessTokenExpirationMinutes = 15, RefreshTokenExpirationHours = 24 })),
            new RefreshTokenHasher(), refresh, new ExternalIdentityRepository(connections), new AdminActor(), email, options, crypto, login);
        user = new(Guid.NewGuid(), new Name("Test"), new Name("Person"), "person@example.com", Role.Admin, passwords.Hash("original-password")) { PasswordChanged = true };
        await users.AddAsync(user);
    }

    [RegistryDatabaseFact]
    public async Task LoginRequiresEmailProofAndInitialPasswordsRemainRestricted()
    {
        var first = await FirstAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.IssueTokens(first));
        var verified = await VerifyAsync();
        var issued = await authentication.IssueTokens(verified);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        Assert.Equal("human", jwt.Claims.Single(c => c.Type == "principal_type").Value);
        Assert.Equal(verified.SecurityStamp, jwt.Claims.Single(c => c.Type == "security_stamp").Value);
        await using var c = connections.CreateConnection();
        await c.ExecuteAsync("UPDATE users SET password_changed=0 WHERE user_id=@id", new { id = user.Id });
        await Assert.ThrowsAsync<PasswordChangeRequiredException>(async () => await authentication.IssueTokens(await VerifyAsync()));
    }

    [RegistryDatabaseFact]
    public async Task ResetProofIsHashedSingleUseAndRevokesTokensCookiesAndPendingCodes()
    {
        var verified = await VerifyAsync(); var tokens = await authentication.IssueTokens(verified);
        var pending = await authentication.BeginLoginAsync(new(user.Email, "original-password")); var code = Code();
        var reset = await ResetRequestAsync();
        Assert.NotEqual(reset.Token, (await resets.FindByUserIdAsync(user.Id.ToString()))!.TokenHash);
        await recovery.ResetAsync(reset);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(reset));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.Refresh(tokens.RefreshToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.IssueTokens(verified));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => login.VerifyAsync(new(pending.ChallengeCredential, code)));
        Assert.False(await new HumanSessionValidator(users, crypto).IsCurrentAsync(EmailLoginTests.Principal(verified)));
        Assert.Null(await resets.FindByUserIdAsync(user.Id.ToString()));
        Assert.True((await users.FindByIdAsync(user.Id))!.PasswordChanged);
    }

    [RegistryDatabaseFact]
    public async Task ResetRejectsExpiredReplacedAndConcurrentUse()
    {
        var first = await ResetRequestAsync(); var replacement = await ResetRequestAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(first));
        await using var c = connections.CreateConnection();
        await c.ExecuteAsync("UPDATE users SET reset_token_expiration=DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 MINUTE) WHERE user_id=@id", new { id = user.Id });
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(replacement));
        replacement = await ResetRequestAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        { try { await recovery.ResetAsync(replacement); return true; } catch (InvalidOperationException) { return false; } }));
        Assert.Single(results, x => x);
    }

    [RegistryDatabaseFact]
    public async Task ReusedPasswordAndValidationFailuresLeaveTheResetProofAndSessionsUsable()
    {
        var tokens = await authentication.IssueTokens(await VerifyAsync());
        var reset = await ResetRequestAsync();
        var storedHash = (await users.FindByIdAsync(user.Id))!.PasswordHash;
        var reused = reset with { NewPassword = "original-password", ConfirmPassword = "original-password" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(reused));
        Assert.Contains("different", error.Message);
        var invalidLink = await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(reused with { Token = crypto.GenerateToken() }));
        Assert.Contains("invalid", invalidLink.Message);
        foreach (var invalid in new[]
        {
            reset with { NewPassword = "short12", ConfirmPassword = "short12" },
            reset with { NewPassword = new string('A', 1025), ConfirmPassword = new string('A', 1025) },
            reset with { ConfirmPassword = "mismatched-password" }
        }) await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(invalid));
        Assert.Equal(storedHash, (await users.FindByIdAsync(user.Id))!.PasswordHash);
        Assert.NotNull(await resets.FindByUserIdAsync(user.Id.ToString()));
        await authentication.Refresh(tokens.RefreshToken);
        // A stale validation snapshot cannot consume the token even if another writer leaves the reset columns intact.
        Assert.False(await resets.ConsumeAsync(user.Id, crypto.Hash(reset.Token), "stale-password-hash", passwords.Hash("different-password")));
        Assert.NotNull(await resets.FindByUserIdAsync(user.Id.ToString()));
        await recovery.ResetAsync(reset);
        Assert.Null(await resets.FindByUserIdAsync(user.Id.ToString()));
    }

    [RegistryDatabaseFact]
    public async Task ResetAcceptsTheExistingMinimumAndMaximumPasswordLengths()
    {
        foreach (var length in new[] { 8, 1024 })
        {
            var reset = await ResetRequestAsync();
            var password = new string('A', length);
            await recovery.ResetAsync(reset with { NewPassword = password, ConfirmPassword = password });
            Assert.True(passwords.Verify(password, (await users.FindByIdAsync(user.Id))!.PasswordHash));
        }
    }

    [RegistryDatabaseFact]
    public async Task RefreshRotationHasOneWinnerAndLegacyOrChangedAccountSessionsCannotRefresh()
    {
        var tokens = await authentication.IssueTokens(await VerifyAsync());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        { try { return await authentication.Refresh(tokens.RefreshToken); } catch (UnauthorizedAccessException) { return null; } }));
        var replacement = Assert.Single(results, x => x is not null)!;
        var legacy = crypto.GenerateToken();
        await refresh.AddAsync(RefreshToken.Create(user.Id, new RefreshTokenHasher().Hash(legacy), DateTime.UtcNow.AddHours(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.Refresh(legacy));
        var edited = (await users.FindByIdAsync(user.Id))!; edited.Email = "changed@example.com"; await users.UpdateAsync(edited);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.Refresh(replacement.RefreshToken));
    }

    [RegistryDatabaseFact]
    public async Task PasswordChangeAndAddressChangeClearResetTokensAndIssuanceChecksTheAddressSnapshot()
    {
        var reset = await ResetRequestAsync();
        await authentication.ChangePasswordAsync(user.Id, new("original-password", "updated-password", "updated-password"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(reset));
        await recovery.SendAsync(user.Email, default);
        var old = (await resets.FindByUserIdAsync(user.Id.ToString()))!;
        var edited = (await users.FindByIdAsync(user.Id))!; edited.Email = "changed@example.com"; await users.UpdateAsync(edited);
        Assert.Null(await resets.FindByUserIdAsync(user.Id.ToString()));
        Assert.False(await resets.CreateAsync(new(user.Id, old.TokenHash, old.ExpiresAt) { Email = user.Email }));
    }

    [RegistryDatabaseFact]
    public async Task ResetFailureRollsBackPasswordAndTokenAndDeletedAccountsCannotRecover()
    {
        var reset = await ResetRequestAsync();
        await using var c = connections.CreateConnection();
        await c.ExecuteAsync("CREATE TRIGGER fail_test BEFORE UPDATE ON refresh_tokens FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Test rollback'");
        await authentication.IssueTokens(await VerifyAsync());
        var hash = (await users.FindByIdAsync(user.Id))!.PasswordHash;
        await Assert.ThrowsAsync<MySqlException>(() => recovery.ResetAsync(reset));
        Assert.Equal(hash, (await users.FindByIdAsync(user.Id))!.PasswordHash);
        Assert.NotNull(await resets.FindByUserIdAsync(user.Id.ToString()));
        await c.ExecuteAsync("DROP TRIGGER fail_test");
        await users.DeleteAsync(user);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ResetAsync(reset));
        email.Body = null; await recovery.SendAsync(user.Email, default); Assert.Null(email.Body);
        await recovery.SendAsync("unknown@example.com", default); Assert.Null(email.Body);
    }

    [RegistryDatabaseFact]
    public async Task HttpFormsEnforceAntiforgeryAndBrowserAndApiLoginBothRequireCodes()
    {
        await new ExternalIdentityRepository(connections).LinkGoogleAsync(user.Id, user.Email, "test-google-subject", user.PasswordHash, default);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(login); builder.Services.AddSingleton(recovery); builder.Services.AddSingleton(authentication); builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IUserRepository>(users); builder.Services.AddSingleton<IAccountSecurityCrypto>(crypto);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuthenticationStateProvider, TestHttpAuthenticationState>();
        builder.Services.AddScoped<HumanSessionValidator>();
        builder.Services.AddAuthentication(AuthenticationSchemes.Cookie)
            .AddCookie(AuthenticationSchemes.Cookie, o => o.Events.OnValidatePrincipal = HumanSessionValidator.ValidateCookieAsync)
            .AddCookie(AuthenticationSchemes.PasswordChange, o => o.Events.OnValidatePrincipal = HumanSessionValidator.ValidateCookieAsync)
            .AddCookie(AuthenticationSchemes.TwoFactor).AddCookie(AuthenticationSchemes.ExternalCookie)
            .AddJwtBearer(AuthenticationSchemes.Bearer, o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = "tests", ValidAudience = "human", ValidateIssuer = true, ValidateAudience = true,
                    ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(new string('K', 64)))
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = HumanSessionValidator.ValidateBearerAsync };
            });
        builder.Services.AddAuthorization(); builder.Services.AddRazorComponents();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CLMS.Web.Controllers.AccountSecurityController).Assembly);
        builder.Services.AddRateLimiter(o =>
        {
            o.AddFixedWindowLimiter("GoogleLink", p => { p.PermitLimit = 100; p.Window = TimeSpan.FromMinutes(1); });
            o.AddFixedWindowLimiter("PasswordRecovery", p => { p.PermitLimit = 100; p.Window = TimeSpan.FromMinutes(1); });
        });
        await using var app = builder.Build(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.UseAntiforgery(); app.MapControllers();
        app.MapGet("/protected", () => "allowed").RequireAuthorization();
        app.MapGet("/test-account-settings", () => new RazorComponentResult<CLMS.Web.Components.Pages.Account.Settings>()).RequireAuthorization();
        app.MapGet("/jwt-protected", () => "allowed").RequireAuthorization(p => p.AddAuthenticationSchemes(AuthenticationSchemes.Bearer).RequireAuthenticatedUser());
        app.MapGet("/test-google-signin", async context => await context.SignInAsync(AuthenticationSchemes.ExternalCookie,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-google-subject"),
                new Claim(ClaimTypes.Email, user.Email), new Claim("google:email_verified", "true"), new Claim("google:hd", "example.com")], "google"))));
        await app.StartAsync(); using var client = app.GetTestClient(); client.BaseAddress = new("https://localhost");
        var cookies = new Dictionary<string, string>();
        void Apply(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var value in values)
            {
                var parts = value.Split(';')[0].Split('=', 2);
                if (parts[1].Length == 0) cookies.Remove(parts[0]); else cookies[parts[0]] = parts[1];
            }
            client.DefaultRequestHeaders.Remove("Cookie");
            if (cookies.Count > 0) client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(p => p.Key + "=" + p.Value)));
        }
        string Antiforgery(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var form = await client.GetAsync("/forgot-password"); Apply(form);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode); Assert.True(form.Headers.CacheControl!.NoStore);
        var anti = Antiforgery(await form.Content.ReadAsStringAsync()); Assert.NotEmpty(anti);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = user.Email }))).StatusCode);
        var sent = await client.PostAsync("/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = user.Email, ["__RequestVerificationToken"] = anti }));
        var sentHtml = await sent.Content.ReadAsStringAsync();
        Assert.Contains("If the account is eligible", sentHtml);
        Assert.Contains("data-recovery-cooldown=\"60\"", sentHtml);
        Assert.Contains("value=\"" + user.Email + "\"", sentHtml);
        var throttledReset = await client.PostAsync("/forgot-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = user.Email, ["__RequestVerificationToken"] = anti }));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttledReset.StatusCode);
        Assert.InRange(throttledReset.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        Assert.Contains("Send in ", await throttledReset.Content.ReadAsStringAsync());
        var started = await client.PostAsync("/auth/cookie-login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = user.Email, ["Password"] = "original-password", ["rememberMe"] = "true", ["__RequestVerificationToken"] = anti })); Apply(started);
        Assert.Equal("/auth/two-factor", started.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/two-factor/request", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["enabled"] = "false", ["__RequestVerificationToken"] = anti }))).StatusCode);
        var pending = await client.GetAsync("/auth/two-factor"); Apply(pending);
        var verified = await client.PostAsync("/auth/two-factor", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["code"] = Code(), ["__RequestVerificationToken"] = Antiforgery(await pending.Content.ReadAsStringAsync()) })); Apply(verified);
        Assert.Equal("/", verified.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/protected")).StatusCode);
        var api = await client.PostAsJsonAsync("/api/auth/login", new AuthenticationRequestDto(user.Email, "original-password"));
        Assert.Equal(HttpStatusCode.Accepted, api.StatusCode);
        var receipt = (await api.Content.ReadFromJsonAsync<EmailLoginReceipt>())!;
        Assert.NotEmpty(receipt.ChallengeCredential);
        var issued = await client.PostAsJsonAsync("/api/auth/two-factor/verify", new VerifyLoginCodeDto(receipt.ChallengeCredential, Code()));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var issuedTokens = (await issued.Content.ReadFromJsonAsync<AuthTokensDto>())!;
        Assert.NotEmpty(issuedTokens.AccessToken);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issuedTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/jwt-protected")).StatusCode);
        var settingsPage = await client.GetAsync("/test-account-settings"); Apply(settingsPage);
        var cookieOptions = app.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(AuthenticationSchemes.Cookie);
        var originalCookie = cookies[cookieOptions.Cookie.Name!];
        var originalTicket = cookieOptions.TicketDataFormat.Unprotect(originalCookie)!;
        var settingsHtml = await settingsPage.Content.ReadAsStringAsync();
        Assert.Contains("Disable 2FA", settingsHtml);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/two-factor/request", new FormUrlEncodedContent(new Dictionary<string, string> { ["enabled"] = "false" }))).StatusCode);
        var requestChange = await client.PostAsync("/account/two-factor/request", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["enabled"] = "false", ["__RequestVerificationToken"] = Antiforgery(settingsHtml) })); Apply(requestChange);
        Assert.Equal(HttpStatusCode.OK, requestChange.StatusCode);
        var confirmationHtml = await requestChange.Content.ReadAsStringAsync();
        var settingCredential = WebUtility.HtmlDecode(Regex.Match(confirmationHtml, "name=\"ChallengeCredential\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(settingCredential); var settingCode = Code();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/two-factor/verify", new VerifyLoginCodeDto(settingCredential, settingCode))).StatusCode);
        var confirmed = await client.PostAsync("/account/two-factor/confirm", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["ChallengeCredential"] = settingCredential, ["Code"] = settingCode, ["CurrentPassword"] = "original-password", ["__RequestVerificationToken"] = Antiforgery(confirmationHtml) })); Apply(confirmed);
        Assert.Equal("/account/settings?message=two_factor_disabled", confirmed.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/protected")).StatusCode);
        var renewedTicket = cookieOptions.TicketDataFormat.Unprotect(cookies[cookieOptions.Cookie.Name!])!;
        Assert.Equal(originalTicket.Properties.ExpiresUtc, renewedTicket.Properties.ExpiresUtc);
        Assert.Equal(originalTicket.Properties.IsPersistent, renewedTicket.Properties.IsPersistent);
        Assert.NotEqual(originalTicket.Principal.FindFirstValue("security_stamp"), renewedTicket.Principal.FindFirstValue("security_stamp"));
        using var otherBrowser = app.GetTestClient(); otherBrowser.BaseAddress = new("https://localhost");
        otherBrowser.DefaultRequestHeaders.Add("Cookie", cookieOptions.Cookie.Name + "=" + originalCookie);
        Assert.Equal(HttpStatusCode.Redirect, (await otherBrowser.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/jwt-protected")).StatusCode);
        var passwordOnlyApi = await client.PostAsJsonAsync("/api/auth/login", new AuthenticationRequestDto(user.Email, "original-password"));
        Assert.Equal(HttpStatusCode.OK, passwordOnlyApi.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await passwordOnlyApi.Content.ReadFromJsonAsync<AuthTokensDto>())!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/jwt-protected")).StatusCode);
        var anonymousForm = await client.GetAsync("/forgot-password"); Apply(anonymousForm);
        var passwordOnlyBrowser = await client.PostAsync("/auth/cookie-login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = user.Email, ["Password"] = "original-password", ["__RequestVerificationToken"] = Antiforgery(await anonymousForm.Content.ReadAsStringAsync()) })); Apply(passwordOnlyBrowser);
        Assert.Equal("/", passwordOnlyBrowser.Headers.Location!.OriginalString);
        Assert.Contains("Enable 2FA", await (await client.GetAsync("/test-account-settings")).Content.ReadAsStringAsync());
        var reset = await ResetRequestAsync(); await recovery.ResetAsync(reset);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/jwt-protected")).StatusCode);
        Apply(await client.GetAsync("/test-google-signin"));
        var google = await client.GetAsync("/auth/google/callback"); Apply(google);
        Assert.Equal("/", google.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/protected")).StatusCode);
        var enablePage = await client.GetAsync("/test-account-settings"); Apply(enablePage);
        var enableRequested = await client.PostAsync("/account/two-factor/request", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["enabled"] = "true", ["__RequestVerificationToken"] = Antiforgery(await enablePage.Content.ReadAsStringAsync()) })); Apply(enableRequested);
        var enableHtml = await enableRequested.Content.ReadAsStringAsync();
        var enableCredential = WebUtility.HtmlDecode(Regex.Match(enableHtml, "name=\"ChallengeCredential\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var enabled = await client.PostAsync("/account/two-factor/confirm", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["ChallengeCredential"] = enableCredential, ["Code"] = Code(), ["CurrentPassword"] = "replacement-password", ["__RequestVerificationToken"] = Antiforgery(enableHtml) })); Apply(enabled);
        Assert.Equal("/login?message=two_factor_updated", enabled.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/protected")).StatusCode);
        Apply(await client.GetAsync("/test-google-signin"));
        google = await client.GetAsync("/auth/google/callback"); Apply(google);
        Assert.Equal("/auth/two-factor", google.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/protected")).StatusCode);
    }

    [RegistryDatabaseFact]
    public async Task PreferencePersistsAndTogglingBackNeverRevivesOldCredentials()
    {
        var original = await VerifyAsync(); var originalTokens = await authentication.IssueTokens(original);
        var pending = await authentication.BeginLoginAsync(new(user.Email, "original-password")); var pendingCode = Code();
        await ResetRequestAsync();
        var disable = await settings.RequestAsync(user.Id, original.SecurityStamp, false);
        await settings.ConfirmAsync(user.Id, original.SecurityStamp, new(disable.Challenge.ChallengeCredential, Code(), "original-password"));
        var loaded = (await new UserRepository(connections).FindByIdAsync(user.Id))!;
        Assert.False(loaded.TwoFactorEnabled);
        Assert.True(passwords.Verify("original-password", loaded.PasswordHash));
        Assert.NotEqual(user.PasswordHash, loaded.PasswordHash);
        Assert.Null(await resets.FindByUserIdAsync(user.Id.ToString()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.Refresh(originalTokens.RefreshToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => login.VerifyAsync(new(pending.ChallengeCredential, pendingCode)));
        var disabled = await FirstAsync(); Assert.False(disabled.TwoFactorEnabled);
        Assert.False((await settings.StatusAsync(user.Id, disabled.SecurityStamp)).Enabled);
        // Ordinary profile updates cannot overwrite this separately managed security setting.
        loaded.TwoFactorEnabled = true; loaded.FirstName = new Name("Renamed"); await users.UpdateAsync(loaded);
        Assert.False((await users.FindByIdAsync(user.Id))!.TwoFactorEnabled);
        var passwordOnly = await authentication.LoginAsync(new(user.Email, "original-password"));
        Assert.NotNull(passwordOnly.Tokens); Assert.Null(passwordOnly.Challenge);
        var enable = await settings.RequestAsync(user.Id, disabled.SecurityStamp, true);
        await settings.ConfirmAsync(user.Id, disabled.SecurityStamp, new(enable.Challenge.ChallengeCredential, Code(), "original-password"));
        var current = (await users.FindByIdAsync(user.Id))!; Assert.True(current.TwoFactorEnabled);
        Assert.False(crypto.StampMatches(current, original.SecurityStamp));
        Assert.False(crypto.StampMatches(current, disabled.SecurityStamp));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.Refresh(passwordOnly.Tokens!.RefreshToken));
        var enabledLogin = await authentication.LoginAsync(new(user.Email, "original-password"));
        Assert.Null(enabledLogin.Tokens); Assert.NotNull(enabledLogin.Challenge);
    }

    [RegistryDatabaseFact]
    public async Task SettingsChangesRequireBothFactorsAndCancellationOrDeliveryFailureDoesNotWrite()
    {
        var current = await FirstAsync(); var change = await settings.RequestAsync(user.Id, current.SecurityStamp, false); var code = Code();
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.ConfirmAsync(user.Id, current.SecurityStamp, new(change.Challenge.ChallengeCredential, code, "wrong-password")));
        Assert.True((await users.FindByIdAsync(user.Id))!.TwoFactorEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.ConfirmAsync(user.Id, current.SecurityStamp, new(change.Challenge.ChallengeCredential, "bad", "original-password")));
        Assert.True((await users.FindByIdAsync(user.Id))!.TwoFactorEnabled);
        await settings.CancelAsync(user.Id, current.SecurityStamp, change.Challenge.ChallengeCredential);
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.ConfirmAsync(user.Id, current.SecurityStamp, new(change.Challenge.ChallengeCredential, code, "original-password")));
        email.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.RequestAsync(user.Id, current.SecurityStamp, false));
        Assert.True((await users.FindByIdAsync(user.Id))!.TwoFactorEnabled);
        Assert.Equal(user.PasswordHash, (await users.FindByIdAsync(user.Id))!.PasswordHash);
    }

    [RegistryDatabaseFact]
    public async Task ConcurrentSettingConfirmationsHaveOneWinnerAndDatabaseFailureRollsBack()
    {
        var current = await VerifyAsync(); var token = await authentication.IssueTokens(current);
        var change = await settings.RequestAsync(user.Id, current.SecurityStamp, false); var code = Code();
        await using var c = connections.CreateConnection();
        await c.ExecuteAsync("CREATE TRIGGER fail_setting BEFORE UPDATE ON refresh_tokens FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Test rollback'");
        await Assert.ThrowsAsync<MySqlException>(() => settings.ConfirmAsync(user.Id, current.SecurityStamp, new(change.Challenge.ChallengeCredential, code, "original-password")));
        Assert.True((await users.FindByIdAsync(user.Id))!.TwoFactorEnabled);
        Assert.Equal(user.PasswordHash, (await users.FindByIdAsync(user.Id))!.PasswordHash);
        Assert.True(await new HumanSessionValidator(users, crypto).IsCurrentAsync(EmailLoginTests.Principal(current)));
        await c.ExecuteAsync("DROP TRIGGER fail_setting");
        var next = await settings.RequestAsync(user.Id, current.SecurityStamp, false); code = Code();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { await settings.ConfirmAsync(user.Id, current.SecurityStamp, new(next.Challenge.ChallengeCredential, code, "original-password")); return true; }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return false; }
        }));
        Assert.Single(results, x => x);
        Assert.False((await users.FindByIdAsync(user.Id))!.TwoFactorEnabled);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.Refresh(token.RefreshToken));
    }

    [RegistryDatabaseFact]
    public async Task PreferenceMigrationPreservesExistingAccountsAndDefaultsToEnabled()
    {
        await using var c = connections.CreateConnection();
        await c.ExecuteAsync("ALTER TABLE users DROP COLUMN two_factor_enabled");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "CLMS.slnx"))) dir = dir.Parent!;
        await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(dir.FullName, "Database/migrations/2026-11-09-user-two-factor-preference.sql")));
        var migrated = (await users.FindByIdAsync(user.Id))!;
        Assert.True(migrated.TwoFactorEnabled); Assert.Equal(user.PasswordHash, migrated.PasswordHash);
        Assert.Equal(user.UserRole, migrated.UserRole);
        await c.ExecuteAsync("INSERT INTO users(user_id,email) VALUES(@id,'new@example.com')", new { id = Guid.NewGuid() });
        Assert.True(await c.ExecuteScalarAsync<bool>("SELECT two_factor_enabled FROM users WHERE email='new@example.com'"));
    }

    private Task<UserClaimsDto> FirstAsync() => authentication.AuthenticateUserAsync(new(user.Email, "original-password"));
    private async Task<UserClaimsDto> VerifyAsync()
    { var r = await authentication.BeginLoginAsync(new(user.Email, "original-password")); return await login.VerifyAsync(new(r.ChallengeCredential, Code())); }
    private string Code() => Regex.Match(email.Body!, @"<strong>(\d{6})</strong>").Groups[1].Value;
    private async Task<ResetPasswordDto> ResetRequestAsync()
    {
        await recovery.SendAsync(user.Email, default);
        var url = WebUtility.HtmlDecode(Regex.Match(email.Body!, "href=\"([^\"]+)\"").Groups[1].Value);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query);
        return new(Guid.Parse(query["userId"]!), query["token"]!, "replacement-password", "replacement-password");
    }
    public async Task DisposeAsync()
    {
        if (admin is null) return;
        if (database.StartsWith("clms_account_test_", StringComparison.Ordinal) && database.Length == 50)
            await admin.ExecuteAsync($"DROP DATABASE `{database}`");
        await admin.DisposeAsync();
    }
    private sealed class AdminActor : ICurrentUser { public Task<Guid> RequireAdminAsync() => Task.FromResult(Guid.Empty); }
    private sealed class TestHttpAuthenticationState(IHttpContextAccessor accessor) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
