using System.Data;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public sealed class WorkstationSessionRepository(MySqlConnectionFactory factory)
{
    public async Task<WorkstationSessionWrite> StartAsync(Guid sessionId, Guid unitId, Guid userId, CancellationToken ct)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // Use the permanent unit row to serialize starts even when there is no open session.
        await using var unit = new MySqlCommand("""
            SELECT computer_unit_id FROM computer_units
            WHERE computer_unit_id = @unit AND deleted_at IS NULL AND is_enabled = TRUE FOR UPDATE
            """, connection, transaction);
        unit.Parameters.AddWithValue("@unit", unitId.ToString());
        if (await unit.ExecuteScalarAsync(ct) is null)
            return WorkstationSessionWrite.NotFound;

        await using var existing = new MySqlCommand("""
            SELECT computer_unit_id, user_id, ended_at FROM workstation_sessions WHERE session_id = @session
            """, connection, transaction);
        existing.Parameters.AddWithValue("@session", sessionId.ToString());
        await using (var reader = await existing.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
                return reader.GetGuid(0) == unitId && reader.GetGuid(1) == userId
                    ? (reader.IsDBNull(2) ? WorkstationSessionWrite.Accepted : WorkstationSessionWrite.Ended) : WorkstationSessionWrite.Conflict;
        }

        await using var insert = new MySqlCommand("""
            INSERT INTO workstation_sessions (session_id, computer_unit_id, user_id, started_at)
            VALUES (@session, @unit, @user, UTC_TIMESTAMP(6))
            """, connection, transaction);
        insert.Parameters.AddWithValue("@session", sessionId.ToString());
        insert.Parameters.AddWithValue("@unit", unitId.ToString());
        insert.Parameters.AddWithValue("@user", userId.ToString());
        try { await insert.ExecuteNonQueryAsync(ct); }
        catch (MySqlException error) when (error.Number == 1062) { return WorkstationSessionWrite.Conflict; }

        await transaction.CommitAsync(ct);
        return WorkstationSessionWrite.Accepted;
    }

    public async Task<WorkstationSessionWrite> EndAsync(Guid sessionId, Guid unitId, CancellationToken ct)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            UPDATE workstation_sessions SET ended_at = COALESCE(ended_at, UTC_TIMESTAMP(6))
            WHERE session_id = @session AND computer_unit_id = @unit
            """, connection);
        command.Parameters.AddWithValue("@session", sessionId.ToString());
        command.Parameters.AddWithValue("@unit", unitId.ToString());
        if (await command.ExecuteNonQueryAsync(ct) != 0)
            return WorkstationSessionWrite.Accepted;

        // Also handle connections configured to return only the number of changed rows.
        command.CommandText = """
            SELECT 1 FROM workstation_sessions WHERE session_id = @session AND computer_unit_id = @unit
            """;
        return await command.ExecuteScalarAsync(ct) is null ? WorkstationSessionWrite.NotFound : WorkstationSessionWrite.Accepted;
    }

    public async Task<IReadOnlyDictionary<Guid, WorkstationSession>> GetActiveAsync(CancellationToken ct)
    {
        var sessions = await ReadAsync("""
            WHERE s.ended_at IS NULL AND u.deleted_at IS NULL
            """, 0, 0, ct);
        return sessions.ToDictionary(session => session.UnitId);
    }

    public Task<IReadOnlyList<WorkstationSession>> GetHistoryAsync(int offset, int limit, CancellationToken ct) =>
        ReadAsync("ORDER BY s.started_at DESC, s.session_id DESC LIMIT @limit OFFSET @offset", offset, limit, ct);

    private async Task<IReadOnlyList<WorkstationSession>> ReadAsync(string suffix, int offset, int limit, CancellationToken ct)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            SELECT s.session_id, s.computer_unit_id, u.unit_name,
                   COALESCE(NULLIF(TRIM(CONCAT_WS(' ', p.first_name, p.last_name)), ''), p.email, p.user_id),
                   s.started_at, s.ended_at
            FROM workstation_sessions s
            INNER JOIN computer_units u ON u.computer_unit_id = s.computer_unit_id
            INNER JOIN users p ON p.user_id = s.user_id
            """ + "\n" + suffix, connection);
        if (limit > 0)
        {
            command.Parameters.AddWithValue("@limit", limit);
            command.Parameters.AddWithValue("@offset", offset);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var sessions = new List<WorkstationSession>();
        while (await reader.ReadAsync(ct))
        {
            sessions.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
                reader.IsDBNull(5) ? null : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
        }
        return sessions;
    }
}
