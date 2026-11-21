using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace CLMS.Tests;

public sealed class AbnormalActivityDatabaseTests
{
    [RegistryDatabaseFact]
    public async Task ConcurrentObservationsConfigurationChangesHistoryAccessAndRetentionAreTransactional()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "CLMS.slnx"))) directory = directory.Parent!;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(directory.FullName, "CLMS.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).Build();
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL")
            ?? config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Configure CLMS_TEST_MYSQL."))
            { Database = "", ConnectionTimeout = 5, AllowUserVariables = true };
        await using var admin = new MySqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        var database = "clms_activity_test_" + Guid.NewGuid().ToString("N");
        await Execute(admin, $"CREATE DATABASE `{database}`");
        try
        {
            builder.Database = database;
            var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = builder.ConnectionString }).Build());
            await using var connection = factory.CreateConnection(); await connection.OpenAsync();
            foreach (var file in new[] { "Database/clms/laboratories.sql", "Database/clms/computer_units.sql",
                         "Database/migrations/2026-11-21-application-monitoring.sql", "Database/migrations/2026-11-22-abnormal-activity-monitoring.sql" })
                await Execute(connection, await File.ReadAllTextAsync(Path.Combine(directory.FullName, file)));
            var unit = Guid.NewGuid(); var otherUnit = Guid.NewGuid();
            await Execute(connection, $"""
                INSERT INTO laboratories (lab_id, lab_name) VALUES (1, 'Lab 1'), (2, 'Lab 2');
                INSERT INTO computer_units (computer_unit_id, unit_name, unit_number, laboratory_id)
                VALUES ('{unit}', 'PC-001', 1, 1), ('{otherUnit}', 'PC-002', 1, 2)
                """);
            var registry = new ApplicationRegistryRepository(factory); var activities = new AbnormalActivityRepository(factory);
            var at = DateTime.UtcNow.AddDays(-40);
            var app = new MonitoredApplication(Guid.NewGuid(), "Original application", "original", ApplicationPlatform.Windows,
                null, true, at.AddHours(-1), at.AddHours(-1)) { FlagForReview = true };
            await registry.SaveAsync(app);
            async Task<ApplicationObservation> Observation(int seconds, bool complete = true, params Guid[] ids) =>
                new(unit, Guid.NewGuid(), at.AddSeconds(seconds), at.AddSeconds(seconds + 1), ApplicationPlatform.Windows, ids, complete)
                    { RegistryRevision = ApplicationRegistryRevision.Compute(await registry.GetAllAsync()) };
            var first = await Observation(0, true, app.Id);
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => new AbnormalActivityRepository(factory).ObserveAsync(first)));
            Assert.Single(concurrent, changed => changed);
            var initial = Assert.Single((await activities.GetPageAsync(new(), [unit])).Items).Activity;
            Assert.False(await activities.ObserveAsync(await Observation(15, true, app.Id)));
            Assert.False(await activities.ObserveAsync(await Observation(5)));
            Assert.False(await activities.ObserveAsync(await Observation(30, false)));
            Assert.Single((await activities.GetPageAsync(new(), [unit])).Items);
            Assert.True(await activities.ObserveAsync(await Observation(45)));
            Assert.Empty((await activities.GetPageAsync(new(), [unit])).Items);
            Assert.Equal(initial.Id, Assert.Single((await activities.GetPageAsync(new(ActiveOnly: false), [unit])).Items).Activity.Id);
            Assert.True(await activities.ObserveAsync(await Observation(60, true, app.Id)));

            app = app with { Name = "Renamed application", UpdatedAt = at.AddSeconds(65) };
            await registry.SaveAsync(app);
            Assert.All((await activities.GetPageAsync(new(ActiveOnly: false), [unit])).Items, row => Assert.Equal("Original application", row.Activity.Details));
            app = app with { FlagForReview = false, UpdatedAt = at.AddSeconds(70) };
            await registry.SaveAsync(app);
            Assert.Empty((await activities.GetPageAsync(new(), [unit])).Items);
            Assert.False(await activities.ObserveAsync(await Observation(80, true, app.Id)));
            app = app with { FlagForReview = true, UpdatedAt = at.AddSeconds(90) }; await registry.SaveAsync(app);
            Assert.True(await activities.ObserveAsync(await Observation(100, true, app.Id)));
            app = app with { IsEnabled = false, UpdatedAt = at.AddSeconds(110) }; await registry.SaveAsync(app);
            Assert.Empty((await activities.GetPageAsync(new(), [unit])).Items);
            app = app with { IsEnabled = true, UpdatedAt = at.AddSeconds(120) }; await registry.SaveAsync(app);
            Assert.True(await activities.ObserveAsync(await Observation(130, true, app.Id)));
            await registry.DeleteAsync(app.Id);
            var history = (await activities.GetPageAsync(new(ActiveOnly: false), [unit])).Items;
            Assert.Equal(4, history.Count);
            Assert.All(history, row => { Assert.NotNull(row.Activity.EndedAt); Assert.Null(row.Activity.ApplicationId); Assert.NotEmpty(row.Activity.Details); });

            var otherApp = app with { Id = Guid.NewGuid(), Name = "Other application", UpdatedAt = at.AddSeconds(140) };
            await registry.SaveAsync(otherApp);
            var other = (await Observation(150, true, otherApp.Id)) with { UnitId = otherUnit };
            Assert.True(await activities.ObserveAsync(other));
            Assert.Single((await activities.GetPageAsync(new(), [otherUnit])).Items);
            Assert.Empty((await activities.GetPageAsync(new(), [unit])).Items);
            Assert.Single((await activities.GetPageAsync(new(ActiveOnly: false, LaboratoryId: 2), [unit, otherUnit])).Items);
            Assert.Empty((await activities.GetPageAsync(new(ActiveOnly: false, Type: AbnormalActivityType.HighCpuUsage), [unit, otherUnit])).Items);
            Assert.Equal(3, await activities.DeleteEndedBeforeAsync(DateTime.UtcNow.AddDays(-30)));
            Assert.Single((await activities.GetPageAsync(new(), [otherUnit])).Items); // Old active row is retained.
            var duplicate = await Assert.ThrowsAsync<MySqlException>(() => Execute(connection, $"""
                INSERT INTO abnormal_activities (activity_id, computer_unit_id, activity_type, application_id, details, detected_at)
                VALUES ('{Guid.NewGuid()}', '{otherUnit}', 'FlaggedApplication', '{otherApp.Id}', 'Duplicate', UTC_TIMESTAMP(6))
                """));
            Assert.Equal(1062, duplicate.Number);
        }
        finally { await Execute(admin, $"DROP DATABASE `{database}`"); }
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
