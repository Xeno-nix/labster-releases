using System.Security.Claims;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Identity.Services;
using CLMS.Application.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Auth;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CLMS.Tests;

public sealed class EmailLoginTests
{
    private readonly AccountSecurityCrypto crypto = new(Options.Create(new JwtOptions { Key = new string('K', 64) }));
    private readonly TestClock clock = new();
    private static User User() => new(Guid.NewGuid(), new Name("Test"), new Name("Person"), "person@example.com", Role.Admin, "test-only-salted-password-hash") { PasswordChanged = true };
    private UserClaimsDto Claims(User user) => new(user.Id, user.Email, user.UserRole, user.PasswordChanged)
        { SecurityStamp = crypto.Stamp(user), TwoFactorEnabled = user.TwoFactorEnabled };

    [Fact]
    public void SecretsAreRandomAndFingerprintsInvalidateOnPasswordEmailRoleOrInitialPasswordChanges()
    {
        var user = User(); var stamp = crypto.Stamp(user);
        var secrets = Enumerable.Range(0, 1000).Select(_ => crypto.GenerateToken()).ToArray();
        Assert.Equal(1000, secrets.Distinct().Count());
        Assert.All(secrets, s => Assert.Matches("^[A-Za-z0-9_-]{43}$", s));
        Assert.Matches("^[0-9]{6}$", crypto.GenerateCode());
        Assert.True(crypto.Verify(secrets[0], crypto.Hash(secrets[0])));
        Assert.False(crypto.Verify(secrets[1], crypto.Hash(secrets[0])));
        Assert.DoesNotContain(user.PasswordHash, stamp);
        user.PasswordHash += "replacement"; Assert.False(crypto.StampMatches(user, stamp));
        stamp = crypto.Stamp(user); user.Email = "other@example.com"; Assert.False(crypto.StampMatches(user, stamp));
        stamp = crypto.Stamp(user); user.UserRole = Role.Faculty; Assert.False(crypto.StampMatches(user, stamp));
        stamp = crypto.Stamp(user); user.PasswordChanged = false; Assert.False(crypto.StampMatches(user, stamp));
    }

    [Fact]
    public async Task ConcurrentCodeConsumptionHasOneWinnerAndStoredProofIsNotAClaimableUserId()
    {
        var store = new EmailLoginChallenges(crypto, clock);
        var delivery = store.Create(Claims(User()));
        Assert.Throws<InvalidOperationException>(() => store.Consume(delivery.User.UserId.ToString(), delivery.Code));
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            try { return store.Consume(delivery.Receipt.ChallengeCredential, delivery.Code); }
            catch (InvalidOperationException) { return null; }
        })));
        Assert.True(Assert.Single(results, x => x is not null)!.TwoFactorVerified);
        Assert.Throws<InvalidOperationException>(() => store.Consume(delivery.Receipt.ChallengeCredential, delivery.Code));
    }

    [Fact]
    public void ResendingRetainsAttemptsAndExpiryAndReplacesTheOldCode()
    {
        var store = new EmailLoginChallenges(crypto, clock);
        var first = store.Create(Claims(User())); var credential = first.Receipt.ChallengeCredential;
        for (var i = 0; i < 4; i++) Assert.Throws<InvalidOperationException>(() => store.Consume(credential, "malformed"));
        Assert.Throws<InvalidOperationException>(() => store.Resend(credential));
        clock.Advance(TimeSpan.FromSeconds(61));
        var resent = store.Resend(credential);
        Assert.Equal(first.Receipt.ExpiresAt, resent.Receipt.ExpiresAt);
        Assert.Throws<InvalidOperationException>(() => store.Consume(credential, "malformed"));
        Assert.Throws<InvalidOperationException>(() => store.Consume(credential, resent.Code));
        var other = store.Create(Claims(User()));
        clock.Advance(TimeSpan.FromSeconds(61)); var replacement = store.Resend(other.Receipt.ChallengeCredential);
        if (replacement.Code != other.Code) Assert.Throws<InvalidOperationException>(() => store.Consume(other.Receipt.ChallengeCredential, other.Code));
        Assert.True(store.Consume(other.Receipt.ChallengeCredential, replacement.Code).TwoFactorVerified);
    }

    [Fact]
    public void ExpirationCancellationAndProcessRestartInvalidatePendingProofs()
    {
        var store = new EmailLoginChallenges(crypto, clock);
        var first = store.Create(Claims(User()));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Throws<InvalidOperationException>(() => store.Consume(first.Receipt.ChallengeCredential, first.Code));
        var next = store.Create(Claims(User())); store.Cancel(next.Receipt.ChallengeCredential);
        Assert.Throws<InvalidOperationException>(() => store.Consume(next.Receipt.ChallengeCredential, next.Code));
        var pending = store.Create(Claims(User()));
        Assert.Throws<InvalidOperationException>(() => new EmailLoginChallenges(crypto, clock).Consume(pending.Receipt.ChallengeCredential, pending.Code));
    }

    [Fact]
    public void SendLimitsApplyAcrossNewChallengesAndResendsForTheSameAccount()
    {
        var store = new EmailLoginChallenges(crypto, clock); var claims = Claims(User());
        for (var i = 0; i < 5; i++) store.Create(claims);
        Assert.Throws<InvalidOperationException>(() => store.Create(claims));
        clock.Advance(TimeSpan.FromMinutes(16));
        store.Create(claims);
    }

    [Fact]
    public async Task EmailOutageCannotLeaveAUsablePendingProofAndAccountChangesRejectAlreadySentCodes()
    {
        var user = User(); var store = new EmailLoginChallenges(crypto, clock);
        var email = new TestEmail { Fail = true }; var users = new TestUsers(user);
        var service = new EmailLoginService(store, users, crypto, email);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAsync(Claims(user)));
        email.Fail = false;
        var receipt = await service.BeginAsync(Claims(user));
        user.PasswordHash += "changed";
        var code = System.Text.RegularExpressions.Regex.Match(email.Body!, @"<strong>(\d{6})</strong>").Groups[1].Value;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VerifyAsync(new(receipt.ChallengeCredential, code)));
    }

    [Fact]
    public async Task CurrentUserAndSessionValidationRejectStaleAdministratorPrincipals()
    {
        var user = User(); var principal = Principal(Claims(user)); var users = new TestUsers(user);
        var context = new DefaultHttpContext { User = principal };
        var current = new CurrentUser(new HttpContextAccessor { HttpContext = context }, new TestState(principal), users, crypto, new TestAuthorizationStore(users));
        Assert.Equal(user.Id, await current.RequireAdminAsync());
        var validator = new HumanSessionValidator(users, crypto);
        Assert.True(await validator.IsCurrentAsync(principal));
        user.PasswordHash += "changed";
        Assert.False(await validator.IsCurrentAsync(principal));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => current.RequireAdminAsync());
    }

    [Fact]
    public async Task RecoveryQueueIsBoundedAndThrottlesAddressesBeforeAccountLookup()
    {
        var queue = new PasswordRecoveryQueue(clock);
        for (var i = 0; i < 200; i++) queue.Enqueue("person" + i + "@example.com");
        using var stop = new CancellationTokenSource();
        await using var reader = queue.ReadAllAsync(stop.Token).GetAsyncEnumerator();
        for (var i = 0; i < 100; i++) Assert.True(await reader.MoveNextAsync());
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.MoveNextAsync());
        var throttled = new PasswordRecoveryQueue(clock);
        throttled.Enqueue("person@example.com"); throttled.Enqueue("person@example.com");
        using var cancel = new CancellationTokenSource(); await using var single = throttled.ReadAllAsync(cancel.Token).GetAsyncEnumerator();
        Assert.True(await single.MoveNextAsync()); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await single.MoveNextAsync());
    }

    [Fact]
    public void SettingsProofCannotAuthenticateAndLoginProofCannotChangeSettingsOrBeClaimedByAnotherUser()
    {
        var store = new EmailLoginChallenges(crypto, clock); var claims = Claims(User());
        var setting = store.CreateSetting(claims, false);
        var credential = setting.Delivery.Receipt.ChallengeCredential;
        Assert.Throws<InvalidOperationException>(() => store.Consume(credential, setting.Delivery.Code));
        Assert.Throws<InvalidOperationException>(() => store.Status(credential));
        Assert.Throws<InvalidOperationException>(() => store.Resend(credential));
        Assert.Throws<InvalidOperationException>(() => store.ConsumeSetting(Guid.NewGuid(), credential, setting.Delivery.Code, true));
        var proof = store.ConsumeSetting(claims.UserId, credential, setting.Delivery.Code, true);
        Assert.False(proof.Enabled); Assert.Equal(claims.UserId, proof.User.UserId);
        Assert.Throws<InvalidOperationException>(() => store.ConsumeSetting(claims.UserId, credential, setting.Delivery.Code, true));
        var login = store.Create(claims);
        Assert.Throws<InvalidOperationException>(() => store.ConsumeSetting(claims.UserId, login.Receipt.ChallengeCredential, login.Code, true));
        Assert.True(store.Consume(login.Receipt.ChallengeCredential, login.Code).TwoFactorVerified);
    }

    [Fact]
    public void IncorrectPasswordsConsumeTheSettingsAttemptBudgetAndResendPreservesIt()
    {
        var store = new EmailLoginChallenges(crypto, clock); var claims = Claims(User());
        var setting = store.CreateSetting(claims, false); var credential = setting.Delivery.Receipt.ChallengeCredential;
        for (var i = 0; i < 4; i++)
            Assert.Throws<InvalidOperationException>(() => store.ConsumeSetting(claims.UserId, credential, setting.Delivery.Code, false));
        clock.Advance(TimeSpan.FromSeconds(61));
        var resent = store.ResendSetting(claims.UserId, credential);
        Assert.Equal(setting.Delivery.Receipt.ExpiresAt, resent.Delivery.Receipt.ExpiresAt);
        Assert.Throws<InvalidOperationException>(() => store.ConsumeSetting(claims.UserId, credential, resent.Delivery.Code, false));
        Assert.Throws<InvalidOperationException>(() => store.ConsumeSetting(claims.UserId, credential, resent.Delivery.Code, true));
    }

    [Fact]
    public void PreferenceIsPartOfTheSessionFingerprint()
    {
        var user = User(); var stamp = crypto.Stamp(user);
        user.TwoFactorEnabled = false;
        Assert.False(crypto.StampMatches(user, stamp));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisablingRenewsOnlyTheConfirmedSnapshotAndPreservesCookieProperties(bool rememberMe)
    {
        var user = User(); user.TwoFactorEnabled = false;
        var confirmed = Claims(user) with { TwoFactorVerified = true };
        var original = new AuthenticationProperties
        {
            IsPersistent = rememberMe, IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), AllowRefresh = false
        };
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(new EmailLoginService(new EmailLoginChallenges(crypto, clock), new TestUsers(user), crypto, new TestEmail()));
        builder.Services.AddAuthentication(AuthenticationSchemes.Cookie).AddCookie(AuthenticationSchemes.Cookie);
        await using var app = builder.Build(); app.UseAuthentication();
        app.MapGet("/renew", async context => await BrowserSignIn.RenewAfterDisablingAsync(context, confirmed, original));
        await app.StartAsync(); using var client = app.GetTestClient(); client.BaseAddress = new("https://localhost");
        var response = await client.GetAsync("/renew");
        var value = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0].Split('=', 2)[1];
        var options = app.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(AuthenticationSchemes.Cookie);
        var ticket = options.TicketDataFormat.Unprotect(value)!;
        Assert.Equal(original.IsPersistent, ticket.Properties.IsPersistent);
        Assert.Equal(original.ExpiresUtc, ticket.Properties.ExpiresUtc);
        Assert.Equal(original.IssuedUtc, ticket.Properties.IssuedUtc);
        Assert.Equal(original.AllowRefresh, ticket.Properties.AllowRefresh);
        var expiredContext = new DefaultHttpContext { RequestServices = app.Services };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => BrowserSignIn.RenewAfterDisablingAsync(expiredContext, confirmed,
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        Assert.False(expiredContext.Response.Headers.ContainsKey("Set-Cookie"));
        user.PasswordHash += "concurrent-reset";
        var staleContext = new DefaultHttpContext { RequestServices = app.Services };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => BrowserSignIn.RenewAfterDisablingAsync(staleContext, confirmed, original));
        Assert.False(staleContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    internal static ClaimsPrincipal Principal(UserClaimsDto claims) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, claims.UserId.ToString()), new Claim(ClaimTypes.Role, claims.UserRole.ToString()),
        new Claim("principal_type", "human"), new Claim("security_stamp", claims.SecurityStamp)], AuthenticationSchemes.Cookie));
    internal sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }
    internal sealed class TestEmail : IEmailService
    {
        public bool Fail;
        public string? Body;
        public Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        { if (Fail) throw new HttpRequestException("Test outage"); Body = body; return Task.CompletedTask; }
    }
    private sealed class TestState(ClaimsPrincipal principal) : AuthenticationStateProvider
    { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
    internal sealed class TestUsers(User user) : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id) => Task.FromResult<User?>(id == user.Id ? user : null);
        public Task<User?> FindByEmailAsync(string email) => Task.FromResult<User?>(email == user.Email ? user : null);
        public Task<IReadOnlyList<User>> FindAllAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> FindAllByRoleAsync(Role role) => throw new NotSupportedException();
        public Task<bool> AddAsync(User value) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(User value) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(User value) => throw new NotSupportedException();
        public Task<int> IncrementFailedLoginAttemptsAsync(Guid id) => throw new NotSupportedException();
        public Task<bool> LockOutAsync(Guid id, DateTime until) => throw new NotSupportedException();
        public Task<bool> ResetFailedLoginAttemptsAsync(Guid id) => throw new NotSupportedException();
        public Task<bool> ChangePasswordAsync(Guid id, string expected, string replacement) => throw new NotSupportedException();
        public Task<bool> ChangeTwoFactorAsync(User expected, bool enabled, string replacement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
