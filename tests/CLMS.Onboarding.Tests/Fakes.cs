using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Identity.Services;
using CLMS.Application.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

internal sealed class Fixture
{
    public FakeUsers Users { get; } = new();
    public FakeIdentities Identities { get; } = new();
    public FakeMail Mail { get; } = new();
    public FakeActor Actor { get; } = new();
    public FakeRefreshTokens Refresh { get; } = new();
    public AccountOptions Options { get; } = new() { PublicUrl = "https://labster.example", SupportEmail = "help@example.com" };
    public AuthenticationService Accounts { get; }
    public Fixture() => Accounts = new(Users, new FakeHasher(), new FakeTokens(), new FakeHasher(), Refresh,
        Identities, Actor, Mail, Options);
    public static User User(string email = "user@usep.edu.ph", bool changed = false) =>
        new(Guid.NewGuid(), new Name("Test"), new Name("User"), email, Role.Faculty, "hash:initial-password") { PasswordChanged = changed };
}

internal sealed class FakeUsers : IUserRepository
{
    public Dictionary<Guid, User> Items { get; } = [];
    public bool FailPasswordUpdate;
    public Task<User?> FindByIdAsync(Guid id) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task<User?> FindByEmailAsync(string email) => Task.FromResult(Items.Values.FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyList<User>> FindAllAsync() => Task.FromResult<IReadOnlyList<User>>(Items.Values.ToList());
    public Task<IReadOnlyList<User>> FindAllByRoleAsync(Role role) => Task.FromResult<IReadOnlyList<User>>(Items.Values.Where(u => u.UserRole == role).ToList());
    public Task<bool> AddAsync(User user) { Items.Add(user.Id, user); return Task.FromResult(true); }
    public Task<bool> UpdateAsync(User user) { Items[user.Id] = user; return Task.FromResult(true); }
    public Task<bool> DeleteAsync(User user) { user.DeletedAt = DateTime.UtcNow; return Task.FromResult(true); }
    public Task<bool> ChangePasswordAsync(Guid id, string expected, string hash)
    {
        var user = Items[id];
        if (FailPasswordUpdate || user.DeletedAt.HasValue || user.PasswordHash != expected) return Task.FromResult(false);
        user.PasswordHash = hash; user.PasswordChanged = true; return Task.FromResult(true);
    }
}

internal sealed class FakeIdentities : IExternalIdentityRepository
{
    public List<ExternalIdentity> Items { get; } = [];
    public Task<ExternalIdentity?> FindAsync(string provider, string subject, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(i => i.Provider == provider && i.ProviderSub == subject));
    public Task<ExternalIdentity?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(i => i.Id == id));
    public Task<bool> CreateAsync(ExternalIdentity identity, CancellationToken ct) { Items.Add(identity); return Task.FromResult(true); }
    public Task LinkGoogleAsync(Guid userId, string email, string subject, string expectedHash, CancellationToken ct = default)
    {
        if (Items.Any(i => i.Provider == "google" && (i.UserId == userId || i.ProviderSub == subject)))
            throw new InvalidOperationException("Already linked.");
        Items.Add(new(Guid.NewGuid(), userId, "google", subject)); return Task.CompletedTask;
    }
}

internal sealed class FakeHasher : IPasswordHasher, IRefreshTokenHasher
{
    public string Hash(string value) => "hash:" + value;
    public bool Verify(string value, string hash) => Hash(value) == hash;
}
internal sealed class FakeTokens : ITokenService
{
    public string GenerateAccessToken(UserClaimsDto user) => "access-token";
    public string GenerateRefreshToken() => Guid.NewGuid().ToString();
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(1);
}
internal sealed class FakeRefreshTokens : IRefreshTokenRepository
{
    public List<RefreshToken> Items { get; } = [];
    public Task<RefreshToken?> FindByHashAsync(string hash) => Task.FromResult(Items.FirstOrDefault(t => t.TokenHash == hash));
    public Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid id) => Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(t => t.UserId == id && t.IsActive).ToList());
    public Task<bool> AddAsync(RefreshToken token) { Items.Add(token); return Task.FromResult(true); }
    public Task<bool> RevokeAsync(Guid id, DateTime at) { Items.Single(t => t.Id == id).Revoke(); return Task.FromResult(true); }
}
internal sealed class FakeActor : ICurrentUser
{
    public bool IsAdmin = true;
    public Task<Guid> RequireAdminAsync() => IsAdmin ? Task.FromResult(Guid.Empty) : throw new UnauthorizedAccessException();
}
internal sealed class FakeMail : IEmailService
{
    public bool Fail;
    public int Calls;
    public string? Recipient, Body;
    public Task SendEmailAsync(string to, string subject, string body, CancellationToken ct)
    {
        Calls++; Recipient = to; Body = body;
        return Fail ? Task.FromException(new HttpRequestException("Delivery failed")) : Task.CompletedTask;
    }
}

internal static class Check
{
    public static void That(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    public static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}

internal sealed class FixedState(ClaimsPrincipal principal) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
}
