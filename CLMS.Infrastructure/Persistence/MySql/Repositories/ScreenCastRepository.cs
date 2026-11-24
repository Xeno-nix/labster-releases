using System.Security.Cryptography;
using System.Text.Json;
using CLMS.Application.Interfaces.Repositories;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public sealed class ScreenCastRepository(MySqlConnectionFactory factory) : IScreenCastRepository
{
    public async Task<int> CreateAsync(Guid initiatedBy, IReadOnlyCollection<Guid> units, DateTime startedAt)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var session = new MySqlCommand("""
            CALL sp_screen_cast_create(@viewer, @units, @at, @token);
            """, connection);
        session.Parameters.AddWithValue("@viewer", initiatedBy.ToString());
        session.Parameters.AddWithValue("@at", startedAt);
        session.Parameters.AddWithValue("@token", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        session.Parameters.AddWithValue("@units", JsonSerializer.Serialize(units));
        return Convert.ToInt32(await session.ExecuteScalarAsync());
    }

    public Task SetConnectionAsync(int sessionId, Guid unitId, bool connected, DateTime at) => ExecuteAsync("""
        CALL sp_screen_cast_set_connection(@session, @unit, @connected, @at);
        """, command =>
        {
            command.Parameters.AddWithValue("@session", sessionId);
            command.Parameters.AddWithValue("@unit", unitId.ToString());
            command.Parameters.AddWithValue("@connected", connected);
            command.Parameters.AddWithValue("@at", at);
        });

    public Task EndAsync(int sessionId, bool failed, DateTime endedAt) => CloseAsync(sessionId, failed, endedAt);
    public Task RecoverInterruptedAsync(DateTime endedAt) => ExecuteAsync(
        "CALL sp_screen_cast_recover_interrupted(@at);", command => command.Parameters.AddWithValue("@at", endedAt));

    private Task CloseAsync(int? sessionId, bool failed, DateTime at) => ExecuteAsync("""
        CALL sp_screen_cast_end(@session, @failed, @at);
        """, command =>
        {
            command.Parameters.AddWithValue("@session", sessionId);
            command.Parameters.AddWithValue("@failed", failed);
            command.Parameters.AddWithValue("@at", at);
        });

    private async Task ExecuteAsync(string sql, Action<MySqlCommand> bind)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        bind(command);
        await command.ExecuteNonQueryAsync();
    }
}
