using CLMS.Application.Identity.Services;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Shared.Dtos;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

internal static class DatabaseChecks
{
    public static async Task<int> Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "CLMS.slnx"))) root = root.Parent ?? throw new Exception("Repository root not found.");
        var connectionString = Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(root.FullName, "CLMS.Web"))
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json")
                .Build();
            connectionString = config.GetConnectionString("DefaultConnection");
        }
        // The migration uses MySQL session variables for conditional index creation.
        var cs = new MySqlConnectionStringBuilder(connectionString) { Database = "", ConnectionTimeout = 5, AllowUserVariables = true };
        var database = "clms_account_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new MySqlConnection(cs.ConnectionString); await admin.OpenAsync();
        // The application's database name is discarded. Only this newly generated database is mutated or dropped.
        await Sql(admin, $"CREATE DATABASE `{database}`");
        try
        {
            cs.Database = database;
            var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = cs.ConnectionString }).Build());
            await using var c = factory.CreateConnection(); await c.OpenAsync();
            await Sql(c, """
                CREATE TABLE users(user_id CHAR(36) PRIMARY KEY,first_name VARCHAR(100),last_name VARCHAR(100),
                middle_name VARCHAR(100),suffix VARCHAR(100),email VARCHAR(254),password_hash TEXT,role_type VARCHAR(30),
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,last_modified_at DATETIME DEFAULT CURRENT_TIMESTAMP,deleted_at DATETIME NULL);
                CREATE TABLE external_identities(id CHAR(36) PRIMARY KEY,user_id CHAR(36),provider VARCHAR(30),provider_sub VARCHAR(255));
                INSERT INTO users(user_id,first_name,last_name,email,password_hash,role_type) VALUES
                ('00000000-0000-0000-0000-000000000001','Existing','User','existing@usep.edu.ph','hash:old-password','Faculty');
                """);
            await Sql(c, await File.ReadAllTextAsync(Path.Combine(root.FullName, "SimpleAccountRegistration.sql")));
            var users = new UserRepository(factory); var identities = new ExternalIdentityRepository(factory);
            var mail = new FakeMail(); var refresh = new FakeRefreshTokens();
            var accounts = new AuthenticationService(users, new FakeHasher(), new FakeTokens(), new FakeHasher(), refresh,
                identities, new FakeActor(), mail, new AccountOptions { PublicUrl = "https://labster.example", SupportEmail = "help@example.com" });
            var passed = 0;
            async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS MySQL " + name); }
            await Test("migration preserves existing passwords and marks existing users changed", async () =>
            {
                var existing = (await users.FindByEmailAsync("existing@usep.edu.ph"))!;
                Check.That(existing.PasswordChanged && existing.PasswordHash == "hash:old-password");
            });
            var created = await accounts.RegisterAsync(new("Registered", "User", null, null, "registered@usep.edu.ph", Role.Faculty, "initial-password"));
            var user = (await users.FindByIdAsync(created.UserId))!;
            await Test("registration and every user projection preserve false password_changed", async () =>
            {
                Check.That(!user.PasswordChanged && created.EmailSent && mail.Calls == 1);
                Check.That(!(await users.FindByEmailAsync(user.Email))!.PasswordChanged);
                Check.That(!(await users.FindAllAsync()).Single(x => x.Id == user.Id).PasswordChanged);
                Check.That(!(await users.FindAllByRoleAsync(Role.Faculty)).Single(x => x.Id == user.Id).PasswordChanged);
            });
            await Test("initial credentials cannot issue JWT tokens", async () =>
            {
                await Check.Reject<PasswordChangeRequiredException>(() => accounts.JwtLogin(new(user.Email, "initial-password")));
            });
            await Test("email failure keeps account and duplicate registration is rejected", async () =>
            {
                mail.Fail = true;
                var failed = await accounts.RegisterAsync(new("Failed", "Email", null, null, "failed@usep.edu.ph", Role.Faculty, "initial-password"));
                Check.That(!failed.EmailSent && await users.FindByIdAsync(failed.UserId) is not null);
                await Check.Reject<InvalidOperationException>(() => accounts.RegisterAsync(new("Failed", "Email", null, null, "failed@usep.edu.ph", Role.Faculty, "initial-password")));
                mail.Fail = false;
            });
            await Test("concurrent registrations cannot duplicate an email", async () =>
            {
                var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
                {
                    try { await accounts.RegisterAsync(new("Race", "User", null, null, "race@usep.edu.ph", Role.Faculty, "initial-password")); return true; }
                    catch (InvalidOperationException) { return false; }
                }));
                Check.That(results.Count(x => x) == 1);
            });
            await Test("concurrent password changes update hash and flag exactly once", async () =>
            {
                var results = await Task.WhenAll(new[] { "hash:new-password", "hash:other-password" }
                    .Select(hash => users.ChangePasswordAsync(user.Id, user.PasswordHash, hash)));
                Check.That(results.Count(x => x) == 1 && (await users.FindByIdAsync(user.Id))!.PasswordChanged);
            });
            await Test("stale profile edits cannot restore an old password", async () =>
            {
                var changedHash = (await users.FindByIdAsync(user.Id))!.PasswordHash;
                user.FirstName = new Name("Updated"); await users.UpdateAsync(user);
                var stored = (await users.FindByIdAsync(user.Id))!;
                Check.That(stored.PasswordHash == changedHash && stored.PasswordChanged && stored.FirstName.Value == "Updated");
                user = stored;
            });
            await Test("Google linking rechecks the current password hash", async () =>
            {
                await Check.Reject<InvalidOperationException>(() => identities.LinkGoogleAsync(user.Id, user.Email, "subject", "stale-hash"));
                Check.That(await identities.FindAsync("google", "subject", default) is null);
            });
            await Test("concurrent repeated Google confirmation creates one link", async () =>
            {
                await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => identities.LinkGoogleAsync(user.Id, user.Email, "subject", user.PasswordHash)));
                Check.That((await identities.FindAsync("google", "subject", default))!.UserId == user.Id);
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM external_identities", c);
                Check.That(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1);
            });
            await Test("existing Google links cannot be replaced or assigned to another user", async () =>
            {
                var other = Fixture.User("other@usep.edu.ph"); await users.AddAsync(other);
                await Check.Reject<InvalidOperationException>(() => identities.LinkGoogleAsync(user.Id, user.Email, "different", user.PasswordHash));
                await Check.Reject<InvalidOperationException>(() => identities.LinkGoogleAsync(other.Id, other.Email, "subject", other.PasswordHash));
            });
            await Test("cross-account Google subject race is rejected by the unique index", async () =>
            {
                var a = Fixture.User("a@usep.edu.ph"); var b = Fixture.User("b@usep.edu.ph");
                await users.AddAsync(a); await users.AddAsync(b);
                var outcomes = await Task.WhenAll(new[] { a, b }.Select(async u =>
                {
                    try { await identities.LinkGoogleAsync(u.Id, u.Email, "race-subject", u.PasswordHash); return true; }
                    catch (InvalidOperationException) { return false; }
                }));
                Check.That(outcomes.Count(x => x) == 1);
            });
            await Test("disabled accounts cannot log in, change password, or link Google", async () =>
            {
                await users.DeleteAsync(user);
                Check.That((await users.FindByEmailAsync(user.Email))!.DeletedAt.HasValue);
                Check.That(!await users.ChangePasswordAsync(user.Id, user.PasswordHash, "hash:denied"));
                await Check.Reject<InvalidOperationException>(() => identities.LinkGoogleAsync(user.Id, user.Email, "denied", user.PasswordHash));
                await Check.Reject<UnauthorizedAccessException>(() => accounts.AuthenticateExternalUserAsync(new("google", "subject", user.Email, true, "usep.edu.ph")));
            });
            return passed;
        }
        finally { await Sql(admin, $"DROP DATABASE `{database}`"); }
    }
    private static async Task Sql(MySqlConnection c, string sql) { await using var cmd = new MySqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
}
