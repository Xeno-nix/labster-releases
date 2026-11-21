using CLMS.Domain.Entities.UnitManagement;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace CLMS.Tests;

public sealed class ApplicationRegistryDatabaseTests
{
    [RegistryDatabaseFact]
    public async Task MigrationAndRepositoryRoundTripEnforceUniquePlatformProcessNames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "CLMS.slnx"))) directory = directory.Parent!;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(directory.FullName, "CLMS.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).Build();
        var connectionString = Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL") ?? config.GetConnectionString("DefaultConnection");
        var builder = new MySqlConnectionStringBuilder(connectionString ?? throw new InvalidOperationException("Configure CLMS_TEST_MYSQL."))
            { Database = "", ConnectionTimeout = 5, AllowUserVariables = true };
        await using var admin = new MySqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        var database = "clms_apps_test_" + Guid.NewGuid().ToString("N");
        await Execute(admin, $"CREATE DATABASE `{database}`");
        try
        {
            builder.Database = database;
            var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = builder.ConnectionString }).Build());
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            var migration = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "Database/migrations/2026-11-21-application-monitoring.sql"));
            await Execute(connection, migration);
            foreach (var file in new[] { "Database/clms/laboratories.sql", "Database/clms/computer_units.sql",
                         "Database/migrations/2026-11-22-abnormal-activity-monitoring.sql" })
                await Execute(connection, await File.ReadAllTextAsync(Path.Combine(directory.FullName, file)));
            var repository = new ApplicationRegistryRepository(factory);
            var now = DateTime.UtcNow;
            var app = new MonitoredApplication(Guid.NewGuid(), "Browser", "chrome", ApplicationPlatform.Windows, null, true, now, now);
            await repository.SaveAsync(app);
            Assert.Equal(app.Id, Assert.Single(await repository.GetAllAsync()).Id);
            await repository.SaveAsync(app with { IsEnabled = false, Name = "Updated" });
            Assert.False(Assert.Single(await repository.GetAllAsync()).IsEnabled);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(app with { Id = Guid.NewGuid(), ProcessName = "CHROME" }));
            await repository.SaveAsync(app with { Id = Guid.NewGuid(), Platform = ApplicationPlatform.MacOS });
            Assert.Equal(2, (await repository.GetAllAsync()).Count);
            await repository.DeleteAsync(app.Id);
            Assert.Equal(ApplicationPlatform.MacOS, Assert.Single(await repository.GetAllAsync()).Platform);
        }
        finally { await Execute(admin, $"DROP DATABASE `{database}`"); }
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
