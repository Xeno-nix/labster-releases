using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public sealed class ApplicationRegistryRepository(MySqlConnectionFactory factory) : IApplicationRegistryRepository
{
    public async Task<IReadOnlyList<MonitoredApplication>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("SELECT * FROM monitored_applications ORDER BY name, application_id", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<MonitoredApplication>();
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetGuid("application_id"), reader.GetString("name"), reader.GetString("process_name"),
                Enum.Parse<ApplicationPlatform>(reader.GetString("platform")),
                reader.IsDBNull(reader.GetOrdinal("icon_path")) ? null : reader.GetString("icon_path"),
                reader.GetBoolean("is_enabled"), DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc))
                { FlagForReview = reader.GetBoolean("flag_for_review") });
        return result;
    }

    public async Task SaveAsync(MonitoredApplication app, CancellationToken ct = default)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var exists = new MySqlCommand("SELECT process_name, platform FROM monitored_applications WHERE application_id = @id FOR UPDATE", connection, transaction);
        exists.Parameters.AddWithValue("@id", app.Id.ToString());
        var updating = false;
        var identityChanged = false;
        await using (var reader = await exists.ExecuteReaderAsync(ct))
            if (await reader.ReadAsync(ct))
            {
                updating = true;
                identityChanged = reader.GetString(0) != app.ProcessName || reader.GetString(1) != app.Platform.ToString();
            }
        await using var command = new MySqlCommand(updating ? """
            UPDATE monitored_applications SET name = @name, process_name = @process, platform = @platform,
                icon_path = @icon, is_enabled = @enabled, flag_for_review = @review, updated_at = @updated WHERE application_id = @id
            """ : """
            INSERT INTO monitored_applications (application_id, name, process_name, platform, icon_path, is_enabled, flag_for_review, created_at, updated_at)
            VALUES (@id, @name, @process, @platform, @icon, @enabled, @review, @created, @updated)
            """, connection, transaction);
        command.Parameters.AddWithValue("@id", app.Id.ToString());
        command.Parameters.AddWithValue("@name", app.Name);
        command.Parameters.AddWithValue("@process", app.ProcessName);
        command.Parameters.AddWithValue("@platform", app.Platform.ToString());
        command.Parameters.AddWithValue("@icon", (object?)app.IconPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@enabled", app.IsEnabled);
        command.Parameters.AddWithValue("@review", app.FlagForReview);
        command.Parameters.AddWithValue("@created", app.CreatedAt);
        command.Parameters.AddWithValue("@updated", app.UpdatedAt);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (MySqlException ex) when (ex.Number == 1062) { throw new ArgumentException("This process is already registered for the selected platform."); }
        if (!app.IsEnabled || !app.FlagForReview || identityChanged)
        {
            await using var close = new MySqlCommand("""
                UPDATE abnormal_activities SET ended_at = GREATEST(detected_at, @at)
                WHERE application_id = @id AND activity_type = 'FlaggedApplication' AND ended_at IS NULL
                """, connection, transaction);
            close.Parameters.AddWithValue("@id", app.Id.ToString());
            close.Parameters.AddWithValue("@at", app.UpdatedAt);
            await close.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var takeLock = new MySqlCommand("SELECT application_id FROM monitored_applications WHERE application_id = @id FOR UPDATE", connection, transaction);
        takeLock.Parameters.AddWithValue("@id", id.ToString());
        await takeLock.ExecuteScalarAsync(ct);
        await using var close = new MySqlCommand("""
            UPDATE abnormal_activities SET ended_at = GREATEST(detected_at, UTC_TIMESTAMP(6))
            WHERE application_id = @id AND activity_type = 'FlaggedApplication' AND ended_at IS NULL
            """, connection, transaction);
        close.Parameters.AddWithValue("@id", id.ToString());
        await close.ExecuteNonQueryAsync(ct);
        // Explicitly detach history in this transaction. SET NULL cascades on the base column of
        // a stored generated column are not supported by all deployed MySQL/MariaDB versions.
        await using var detach = new MySqlCommand("UPDATE abnormal_activities SET application_id = NULL WHERE application_id = @id", connection, transaction);
        detach.Parameters.AddWithValue("@id", id.ToString());
        await detach.ExecuteNonQueryAsync(ct);
        await using var command = new MySqlCommand("DELETE FROM monitored_applications WHERE application_id = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
