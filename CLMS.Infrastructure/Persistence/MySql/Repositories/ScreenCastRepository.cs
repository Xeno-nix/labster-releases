using System.Security.Cryptography;
using CLMS.Application.Interfaces.Repositories;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public sealed class ScreenCastRepository(MySqlConnectionFactory factory) : IScreenCastRepository
{
    public async Task<int> CreateAsync(Guid initiatedBy, IReadOnlyCollection<Guid> units, DateTime startedAt)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var session = new MySqlCommand("""
            INSERT INTO screen_cast_sessions (initiated_by, status, started_at, session_token)
            VALUES (@viewer, 'active', @at, @token);
            """, connection, transaction);
        session.Parameters.AddWithValue("@viewer", initiatedBy.ToString());
        session.Parameters.AddWithValue("@at", startedAt);
        session.Parameters.AddWithValue("@token", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        await session.ExecuteNonQueryAsync();
        var id = checked((int)session.LastInsertedId);
        foreach (var unit in units)
        {
            await using var command = new MySqlCommand("""
                INSERT INTO screen_cast_connections (cast_session_id, computer_unit_id, status)
                VALUES (@session, @unit, 'disconnected');
                """, connection, transaction);
            command.Parameters.AddWithValue("@session", id);
            command.Parameters.AddWithValue("@unit", unit.ToString());
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return id;
    }

    public Task SetConnectionAsync(int sessionId, Guid unitId, bool connected, DateTime at) => ExecuteAsync("""
        UPDATE screen_cast_connections c
        INNER JOIN screen_cast_sessions s ON s.cast_session_id = c.cast_session_id
        SET c.status = @status,
            c.started_at = CASE WHEN @status = 'connected' THEN @at ELSE c.started_at END,
            c.ended_at = CASE WHEN @status = 'connected' THEN NULL ELSE @at END
        WHERE c.cast_session_id = @session AND c.computer_unit_id = @unit AND s.status = 'active';
        """, command =>
        {
            command.Parameters.AddWithValue("@session", sessionId);
            command.Parameters.AddWithValue("@unit", unitId.ToString());
            command.Parameters.AddWithValue("@status", connected ? "connected" : "disconnected");
            command.Parameters.AddWithValue("@at", at);
        });

    public Task EndAsync(int sessionId, bool failed, DateTime endedAt) => CloseAsync(sessionId, failed, endedAt);
    public Task RecoverInterruptedAsync(DateTime endedAt) => CloseAsync(null, true, endedAt);

    private Task CloseAsync(int? sessionId, bool failed, DateTime at) => ExecuteAsync("""
        UPDATE screen_cast_connections c
        INNER JOIN screen_cast_sessions s ON s.cast_session_id = c.cast_session_id
        SET c.status = 'disconnected', c.ended_at = COALESCE(c.ended_at, @at)
        WHERE s.status = 'active' AND (@session IS NULL OR s.cast_session_id = @session);
        UPDATE screen_cast_sessions SET status = @status, ended_at = @at
        WHERE status = 'active' AND (@session IS NULL OR cast_session_id = @session);
        """, command =>
        {
            command.Parameters.AddWithValue("@session", sessionId);
            command.Parameters.AddWithValue("@status", failed ? "failed" : "ended");
            command.Parameters.AddWithValue("@at", at);
        });

    private async Task ExecuteAsync(string sql, Action<MySqlCommand> bind)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new MySqlCommand(sql, connection, transaction);
        bind(command);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
