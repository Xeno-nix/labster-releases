using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using Dapper;
using MySqlConnector;
using static DatabaseChecks;

internal static class IdentityChecks
{
    internal static async Task Run(MySqlConnection db, MySqlConnectionFactory factory)
    {
        var users = new UserRepository(factory);
        var user = new User(Guid.NewGuid(), new Name("Identity"), new Name("Test"), "identity@example.test", Role.Faculty, "hash:old");
        await users.AddAsync(user);
        var tokens = new RefreshTokenRepository(factory);
        var first = RefreshToken.Create(user.Id, "token:first", DateTime.UtcNow.AddHours(1));
        Require(await tokens.AddAsync(first) && (await tokens.FindByHashAsync(first.TokenHash))?.UserId == user.Id, "refresh token insert and hash lookup");
        var changes = await Task.WhenAll(new[] { "hash:new", "hash:other" }.Select(hash => users.ChangePasswordAsync(user.Id, "hash:old", hash)));
        Require(changes.Count(x => x) == 1, "only one concurrent password change succeeds");
        Require((await tokens.GetActiveByUserIdAsync(user.Id)).Count == 0, "password change revokes refresh tokens atomically");
        var current = (await users.FindByIdAsync(user.Id))!;
        user.FirstName = new Name("Profile");
        await users.UpdateAsync(user);
        Require((await users.FindByIdAsync(user.Id))!.PasswordHash == current.PasswordHash, "stale profile cannot restore password");
        current = (await users.FindByIdAsync(user.Id))!;
        Require(!await tokens.TryIssueAsync(user, RefreshToken.Create(user.Id, "token:stale", DateTime.UtcNow.AddHours(1)), null, 2), "stale security snapshot cannot issue refresh token");
        Require(await tokens.TryIssueAsync(current, RefreshToken.Create(user.Id, "token:valid", DateTime.UtcNow.AddHours(1)), null, 2), "valid security snapshot issues refresh token");
        Require(await tokens.TryIssueAsync(current, RefreshToken.Create(user.Id, "token:rotated", DateTime.UtcNow.AddHours(1)), "token:valid", 2), "refresh rotation consumes predecessor");
        Require(!await tokens.TryIssueAsync(current, RefreshToken.Create(user.Id, "token:replay", DateTime.UtcNow.AddHours(1)), "token:valid", 2), "refresh predecessor replay rejected");
        for (var i = 0; i < 3; i++)
            Require(await tokens.TryIssueAsync(current, RefreshToken.Create(user.Id, "token:limit" + i, DateTime.UtcNow.AddHours(1)), null, 2), "bounded refresh issue " + i);
        Require((await tokens.GetActiveByUserIdAsync(user.Id)).Count == 2, "refresh session limit enforced");
        Require(await users.ChangeTwoFactorAsync(current, false, current.PasswordHash), "two-factor compare and swap");
        Require(!await users.ChangeTwoFactorAsync(current, false, current.PasswordHash) && (await tokens.GetActiveByUserIdAsync(user.Id)).Count == 0, "stale two-factor update rejected and sessions revoked");

        var reset = new ResetTokenRepository(factory);
        current = (await users.FindByIdAsync(user.Id))!;
        var hash = new string('A', 64);
        Require(await reset.CreateAsync(new ResetToken(user.Id, hash, DateTime.UtcNow.AddMinutes(10)) { Email = current.Email }), "reset hash stored with address snapshot");
        Require((await reset.FindByUserIdAsync(user.Id.ToString()))?.TokenHash == hash, "reset lookup mapping");
        var resets = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => reset.ConsumeAsync(user.Id, hash, current.PasswordHash, "hash:reset")));
        Require(resets.Count(x => x) == 1 && await reset.FindByUserIdAsync(user.Id.ToString()) is null, "reset token consumed only once");
        current = (await users.FindByIdAsync(user.Id))!;
        var identities = new ExternalIdentityRepository(factory);
        await identities.LinkGoogleAsync(user.Id, current.Email, "test-subject", current.PasswordHash);
        await identities.LinkGoogleAsync(user.Id, current.Email, "test-subject", current.PasswordHash);
        var identity = await identities.FindAsync("google", "test-subject", default);
        Require(identity?.UserId == user.Id && (await identities.FindByIdAsync(identity.Id, default))?.ProviderSub == "test-subject", "Google linking is idempotent and mapped");
        try { await identities.LinkGoogleAsync(user.Id, current.Email, "other-subject", current.PasswordHash); throw new Exception("Conflicting identity accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS conflicting Google identity rejected"); }
        var manual = new ExternalIdentity(Guid.NewGuid(), user.Id, "test", "manual-subject");
        Require(await identities.CreateAsync(manual, default), "external identity explicit creation");
        var revoke = RefreshToken.Create(user.Id, "token:revoke", DateTime.UtcNow.AddHours(1));
        await tokens.AddAsync(revoke);
        Require(await tokens.RevokeAsync(revoke.Id, DateTime.UtcNow) && !await tokens.RevokeAsync(revoke.Id, DateTime.UtcNow), "revoke returns target row count");
        Require(await users.DeleteAsync(current), "user soft deletion");
        Require(await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM vw_users WHERE user_id=@id", new { id = user.Id }) == 1, "reporting view preserves deleted users");
    }
}
