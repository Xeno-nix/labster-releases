using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Repositories;
using CLMS.Application.DTOs.Monitoring;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace CLMS.Tests;

public sealed class WorkstationSessionDatabaseTests
{
    [RegistryDatabaseFact]
    public async Task SessionsAreBoundToUnitAndUserAndSafeToRetry()
    {
        var settings = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL")
            ?? "Server=localhost;User ID=root;Password=;") { Database = "", ConnectionTimeout = 5 };
        await using var server = new MySqlConnection(settings.ConnectionString);
        await server.OpenAsync();
        var database = "clms_session_test_" + Guid.NewGuid().ToString("N");
        await Execute(server, $"CREATE DATABASE `{database}`");
        try
        {
            settings.Database = database;
            var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = settings.ConnectionString }).Build());
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            await Execute(connection, """
                CREATE TABLE computer_units(computer_unit_id CHAR(36) PRIMARY KEY, unit_name VARCHAR(100),
                    deleted_at DATETIME NULL, is_enabled BOOLEAN NOT NULL DEFAULT TRUE);
                CREATE TABLE users(user_id CHAR(36) PRIMARY KEY, first_name VARCHAR(50), last_name VARCHAR(50), email VARCHAR(100));
                """);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "CLMS.slnx"))) root = root.Parent!;
            await Execute(connection, await File.ReadAllTextAsync(Path.Combine(root.FullName,
                "Database/migrations/2026-11-19-workstation-sessions.sql")));

            var unit = Guid.NewGuid();
            var otherUnit = Guid.NewGuid();
            var user = Guid.NewGuid();
            var otherUser = Guid.NewGuid();
            await Execute(connection, $"""
                INSERT INTO computer_units(computer_unit_id, unit_name) VALUES ('{unit}', 'PC 1'), ('{otherUnit}', 'PC 2');
                INSERT INTO users VALUES ('{user}', 'First', 'User', 'first@example.test'),
                    ('{otherUser}', 'Next', 'User', 'next@example.test');
                """);

            var repository = new WorkstationSessionRepository(factory);
            var first = Guid.NewGuid();
            var competing = Guid.NewGuid();
            var attempts = await Task.WhenAll(repository.StartAsync(first, unit, user, default),
                repository.StartAsync(competing, unit, otherUser, default));
            Assert.Single(attempts, x => x == WorkstationSessionWrite.Accepted);
            Assert.Single(attempts, x => x == WorkstationSessionWrite.Conflict);
            var firstWon = attempts[0] == WorkstationSessionWrite.Accepted;
            var active = firstWon ? first : competing;
            var activeUser = firstWon ? user : otherUser;
            var nextUser = firstWon ? otherUser : user;
            Assert.Equal(WorkstationSessionWrite.Accepted, await repository.StartAsync(active, unit, activeUser, default));
            Assert.Equal(WorkstationSessionWrite.Conflict, await repository.StartAsync(active, otherUnit, activeUser, default));
            Assert.Equal(WorkstationSessionWrite.NotFound, await repository.EndAsync(active, otherUnit, default));
            Assert.Equal(WorkstationSessionWrite.Accepted, await repository.EndAsync(active, unit, default));

            var next = Guid.NewGuid();
            Assert.Equal(WorkstationSessionWrite.Accepted, await repository.StartAsync(next, unit, nextUser, default));
            Assert.Equal(WorkstationSessionWrite.Accepted, await repository.EndAsync(active, unit, default));
            Assert.Equal(WorkstationSessionWrite.Ended, await repository.StartAsync(active, unit, activeUser, default));
            Assert.Equal(next, (await repository.GetActiveAsync(default))[unit].SessionId);
            var history = await repository.GetHistoryAsync(0, 10, default);
            Assert.Equal(2, history.Count);
            Assert.Equal(next, history[0].SessionId);
            Assert.Null(history[0].EndedUtc);
            Assert.NotNull(history[1].EndedUtc);
        }
        finally
        {
            await Execute(server, $"DROP DATABASE `{database}`");
        }
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
