using CLMS.Infrastructure.DatabaseMaintenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using CLMS.Web.Components.Pages.Admin;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Security.Claims;
using Xunit;

namespace CLMS.Tests;

public class DatabaseBackupTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Instructor", false)]
    public async Task OnlyAdminsSeeDatabaseOperations(string role, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<CLMS.Application.Identity.Interfaces.ICurrentUser>(new TestCurrentUser()); services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TestPrivilegeHandler>();
        services.AddAuthorizationCore(CLMS.Web.Authentication.PrivilegePolicies.Add);
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthentication(role));
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=unused;" }).Build());
        services.Configure<DatabaseBackupOptions>(options => options.Directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        services.AddSingleton<DatabaseBackupService>();
        services.AddSingleton<CLMS.Application.Authorization.IAuthorizationChanges, TestAuthorizationChanges>();
        services.AddScoped<CLMS.Web.Services.ManualDatabaseBackups>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<DatabaseBackups>()).ToHtmlString());
        Assert.Equal(allowed, html.Contains("Back up now"));
        Assert.Equal(!allowed, html.Contains("Administrator access is required"));
    }

    [Theory]
    [InlineData("2026-10-26T17:59:00Z", "2026-10-26T18:00:00Z")]
    [InlineData("2026-10-26T18:00:00Z", "2026-10-27T18:00:00Z")]
    [InlineData("2026-10-26T20:00:00+08:00", "2026-10-26T18:00:00Z")]
    [InlineData("2026-10-26T01:59:00+08:00", "2026-10-25T18:00:00Z")]
    [InlineData("2026-10-26T02:00:00+08:00", "2026-10-26T18:00:00Z")]
    public void ScheduleUsesNextPhilippineTimeOccurrence(string now, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected), new DatabaseBackupOptions().NextRun(DateTimeOffset.Parse(now)));

    [DatabaseBackupIntegration]
    public async Task NativeToolsRoundTripWithSafetySnapshotRetentionAndIntegrityChecks()
    {
        var connection = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLMS_BACKUP_TEST_SERVER")!);
        connection.AllowUserVariables = true;
        connection.Database = "";
        var database = "clms_backup_test_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), database);
        await using var admin = new MySqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await Execute(admin, $"CREATE DATABASE `{database}`");
        try
        {
            connection.Database = database;
            var options = new DatabaseBackupOptions
            {
                Directory = directory, KeepLast = 2,
                DumpExecutable = Environment.GetEnvironmentVariable("CLMS_BACKUP_TEST_DUMP") ?? "mysqldump",
                ClientExecutable = Environment.GetEnvironmentVariable("CLMS_BACKUP_TEST_CLIENT") ?? "mysql"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString }).Build();
            var backups = new DatabaseBackupService(config, Options.Create(options), NullLogger<DatabaseBackupService>.Instance);
            await using var db = new MySqlConnection(connection.ConnectionString);
            await db.OpenAsync();
            await Execute(db, "CREATE TABLE sample (id INT PRIMARY KEY, note TEXT, payload BLOB) ENGINE=InnoDB; INSERT INTO sample VALUES (1,'Original café 漢字',X'00FF01')");
            var legacy = await backups.CreateAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => backups.RestoreAsync(legacy.FileName));
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "CLMS.slnx"))) root = root.Parent!;
            foreach (var file in new[] { "Database/clms/users.sql", "Database/clms/refresh_tokens.sql", "Database/migrations/2026-11-23-hybrid-authorization.sql" })
                await Execute(db, await File.ReadAllTextAsync(Path.Combine(root.FullName, file)));
            var withoutRoot = await backups.CreateAsync();
            await Execute(db, $"INSERT INTO users(user_id,first_name,last_name,email,password_hash,role_type,password_changed) VALUES ('{Guid.NewGuid()}','Root','Test','root@example.test','test-hash','superadmin',TRUE)");
            await Assert.ThrowsAsync<InvalidOperationException>(() => backups.RestoreAsync(withoutRoot.FileName));
            Assert.DoesNotContain(backups.List(), b => b.Kind == "pre-restore");
            var original = await backups.CreateAsync();
            await Execute(db, "UPDATE sample SET note='Changed'; INSERT INTO sample VALUES (2,'Extra',NULL)");
            var recovery = await backups.RestoreAsync(original.FileName);
            Assert.Equal("pre-restore", recovery.Kind);
            Assert.Equal("Original café 漢字", await Scalar(db, "SELECT note FROM sample WHERE id=1"));
            Assert.Equal("00FF01", await Scalar(db, "SELECT HEX(payload) FROM sample WHERE id=1"));
            Assert.Equal("1", await Scalar(db, "SELECT COUNT(*) FROM sample"));
            await backups.RestoreAsync(recovery.FileName);
            Assert.Equal("Changed", await Scalar(db, "SELECT note FROM sample WHERE id=1"));
            Assert.Equal("2", await Scalar(db, "SELECT COUNT(*) FROM sample"));

            await File.AppendAllTextAsync(Path.Combine(directory, original.FileName), "\n-- modified");
            await Assert.ThrowsAsync<InvalidOperationException>(() => backups.RestoreAsync(original.FileName));
            Assert.Equal("Changed", await Scalar(db, "SELECT note FROM sample WHERE id=1"));
            await Assert.ThrowsAsync<ArgumentException>(() => backups.RestoreAsync("../other.sql"));

            await backups.CreateAsync();
            await backups.CreateAsync();
            Assert.Equal(2, backups.List().Count(b => b.Kind != "pre-restore"));
            Assert.Equal(2, backups.List().Count(b => b.Kind == "pre-restore"));
            options.DumpExecutable = Path.Combine(directory, "missing-tool");
            await Assert.ThrowsAsync<InvalidOperationException>(() => backups.CreateAsync());
            Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
            Assert.Equal(4, backups.List().Count);
        }
        finally
        {
            MySqlConnection.ClearAllPools();
            await Execute(admin, $"DROP DATABASE `{database}`");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> Scalar(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private sealed class TestAuthentication(string role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"))));
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
}

public sealed class DatabaseBackupIntegrationAttribute : FactAttribute
{
    public DatabaseBackupIntegrationAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLMS_BACKUP_TEST_SERVER")))
            Skip = "Set CLMS_BACKUP_TEST_SERVER and native tool paths to run against a disposable database.";
    }
}
