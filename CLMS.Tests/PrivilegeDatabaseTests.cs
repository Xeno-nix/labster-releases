using CLMS.Application.Authorization;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace CLMS.Tests;

public sealed class PrivilegeDatabaseTests
{
    [RegistryDatabaseFact]
    public async Task MigrationOverridesProtectedAccountsConcurrentDemotionsAndAuditAreEnforced()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "CLMS.slnx"))) directory = directory.Parent!;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(directory.FullName, "CLMS.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).Build();
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL")
            ?? config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Configure CLMS_TEST_MYSQL."))
            { Database = "", AllowUserVariables = true, ConnectionTimeout = 5 };
        await using var admin = new MySqlConnection(builder.ConnectionString); await admin.OpenAsync();
        var database = "clms_auth_test_" + Guid.NewGuid().ToString("N");
        await Execute(admin, $"CREATE DATABASE `{database}`");
        try
        {
            builder.Database = database;
            var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = builder.ConnectionString }).Build());
            await using var db = factory.CreateConnection(); await db.OpenAsync();
            foreach (var file in new[] { "Database/clms/users.sql", "Database/clms/refresh_tokens.sql", "Database/migrations/2026-11-23-hybrid-authorization.sql" })
                await Execute(db, await File.ReadAllTextAsync(Path.Combine(directory.FullName, file)));
            var users = new UserRepository(factory); var permissions = new AuthorizationRepository(factory);
            var first = PrivilegeAuthorizationTests.Account(Role.Admin); first.Email = "first@example.test";
            var second = PrivilegeAuthorizationTests.Account(Role.Admin); second.Email = "second@example.test";
            var faculty = PrivilegeAuthorizationTests.Account(); faculty.Email = "faculty@example.test";
            await users.AddAsync(first); await users.AddAsync(second); await users.AddAsync(faculty);
            foreach (var role in await permissions.GetRolesAsync()) Assert.True(role.Privileges.SetEquals(Privileges.Defaults(role.Role)));
            Assert.True(await permissions.HasPrivilegeAsync(faculty.Id, Privileges.UnitsMonitor));
            Assert.False(await permissions.HasPrivilegeAsync(faculty.Id, Privileges.UsersView));
            await permissions.ProvisionSuperAdminAsync(first.Id);
            Assert.True((await permissions.GetEffectivePrivilegesAsync(first.Id)).SetEquals(Privileges.Defaults(Role.SuperAdmin)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => permissions.ProvisionSuperAdminAsync(second.Id));
            await permissions.SetOverrideAsync(first.Id, faculty.Id, Privileges.UsersView, true);
            Assert.True(await permissions.HasPrivilegeAsync(faculty.Id, Privileges.UsersView));
            await permissions.SetOverrideAsync(first.Id, faculty.Id, Privileges.UnitsMonitor, false);
            Assert.False(await permissions.HasPrivilegeAsync(faculty.Id, Privileges.UnitsMonitor));
            await permissions.SetOverrideAsync(first.Id, faculty.Id, Privileges.UnitsMonitor, null);
            Assert.True(await permissions.HasPrivilegeAsync(faculty.Id, Privileges.UnitsMonitor));
            var defaults = Privileges.Defaults(Role.Faculty).Where(p => p != Privileges.InventoryManage).ToHashSet();
            await permissions.SetRolePrivilegesAsync(first.Id, Role.Faculty, defaults);
            Assert.False(await permissions.HasPrivilegeAsync(faculty.Id, Privileges.InventoryManage));
            Assert.Equal(Role.Faculty, (await users.FindByIdAsync(faculty.Id))!.UserRole);
            await Assert.ThrowsAsync<ArgumentException>(() => permissions.SetOverrideAsync(first.Id, faculty.Id, "unknown", true));
            await Assert.ThrowsAsync<ArgumentException>(() => permissions.SetRolePrivilegesAsync(first.Id, Role.SuperAdmin, new HashSet<string>()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SetOverrideAsync(first.Id, first.Id, Privileges.UnitsView, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SetRolePrivilegesAsync(second.Id, Role.Faculty, defaults));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SetOverrideAsync(second.Id, faculty.Id, Privileges.UsersDelete, true));
            var protectedUser = (await users.FindByIdAsync(first.Id))!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SaveManagedUserAsync(second.Id, protectedUser, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.DeleteManagedUserAsync(second.Id, first.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.DeleteManagedUserAsync(first.Id, first.Id));
            protectedUser.UserRole = Role.Admin;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SaveManagedUserAsync(first.Id, protectedUser, false));
            var escalation = (await users.FindByIdAsync(faculty.Id))!; escalation.UserRole = Role.SuperAdmin;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SaveManagedUserAsync(second.Id, escalation, false));
            await permissions.SetOverrideAsync(first.Id, second.Id, Privileges.UsersDelete, false);
            escalation.UserRole = Role.Admin;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => permissions.SaveManagedUserAsync(second.Id, escalation, false));
            await permissions.SetOverrideAsync(first.Id, second.Id, Privileges.UnitsMonitor, false);
            second.UserRole = Role.SuperAdmin;
            await permissions.SaveManagedUserAsync(first.Id, second, false);
            Assert.Empty((await permissions.GetAsync(second.Id))!.Overrides);
            var one = (await users.FindByIdAsync(first.Id))!; one.UserRole = Role.Admin;
            var two = (await users.FindByIdAsync(second.Id))!; two.UserRole = Role.Admin;
            var outcomes = await Task.WhenAll(Demote(first.Id, two), Demote(second.Id, one));
            Assert.Equal(1, outcomes.Count(s => s));
            Assert.Equal(1, (await users.FindAllAsync()).Count(u => u.UserRole == Role.SuperAdmin));
            Assert.False(await permissions.HasPrivilegeAsync(Guid.NewGuid(), Privileges.UnitsView));
            Assert.False(await permissions.HasPrivilegeAsync(first.Id, "unknown"));
            await users.LockOutAsync(faculty.Id, DateTime.UtcNow.AddMinutes(5));
            Assert.Empty(await permissions.GetEffectivePrivilegesAsync(faculty.Id));
            await using var audits = new MySqlCommand("SELECT COUNT(*) FROM authorization_audit WHERE is_allowed=FALSE", db);
            Assert.True(Convert.ToInt32(await audits.ExecuteScalarAsync()) >= 8);
            await using var cleared = new MySqlCommand("SELECT COUNT(*) FROM authorization_audit WHERE operation='override_cleared_on_promotion'", db);
            Assert.Equal(2, Convert.ToInt32(await cleared.ExecuteScalarAsync()));
            // Audit failure must roll back the privilege mutation, rather than commit an unaudited change.
            var survivor = (await users.FindAllAsync()).Single(u => u.UserRole == Role.SuperAdmin);
            await Execute(db, "DROP TABLE authorization_audit");
            await Assert.ThrowsAsync<MySqlException>(() => permissions.SetRolePrivilegesAsync(survivor.Id, Role.Faculty, new HashSet<string>()));
            Assert.True((await permissions.GetRolesAsync()).Single(r => r.Role == Role.Faculty).Privileges.SetEquals(defaults));

            async Task<bool> Demote(Guid actor, CLMS.Domain.Entities.Identity.User target)
            {
                try { await new AuthorizationRepository(factory).SaveManagedUserAsync(actor, target, false); return true; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }
        finally { await Execute(admin, $"DROP DATABASE `{database}`"); }
    }
    private static async Task Execute(MySqlConnection connection, string sql) { await using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
}
