using System.Data;
using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public sealed class AbnormalActivityRepository(MySqlConnectionFactory factory) : IAbnormalActivityRepository
{
    public async Task<bool> ObserveAsync(ApplicationObservation observation, CancellationToken ct = default)
    {
        var requested = new DateTime(observation.RequestedAt.Ticks / 10 * 10, DateTimeKind.Utc);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var unit = new MySqlCommand("""
            SELECT last_activity_scan_at, last_activity_scan_id FROM computer_units
            WHERE computer_unit_id = @unit AND deleted_at IS NULL AND is_enabled = TRUE FOR UPDATE
            """, connection, transaction);
        unit.Parameters.AddWithValue("@unit", observation.UnitId.ToString());
        await using (var reader = await unit.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            if ((!reader.IsDBNull(0) && reader.GetDateTime(0) >= requested)
                || (!reader.IsDBNull(1) && reader.GetGuid(1) == observation.RequestId)) return false;
        }

        // Shared registry locks serialize observations with administrative edits/deletes. Those writes
        // close affected activities in the same transaction as the configuration change.
        await using var registry = new MySqlCommand("""
            SELECT application_id, name, platform, updated_at, is_enabled, flag_for_review, process_name
            FROM monitored_applications ORDER BY application_id LOCK IN SHARE MODE
            """, connection, transaction);
        var applications = new List<MonitoredApplication>();
        await using (var reader = await registry.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                applications.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(6),
                    Enum.Parse<ApplicationPlatform>(reader.GetString(2)), null, reader.GetBoolean(4), default, Utc(reader.GetDateTime(3)))
                    { FlagForReview = reader.GetBoolean(5) });
            }
        }
        // A failed/missed registry refresh cannot close or open detections using obsolete matches.
        if (observation.RegistryRevision != ApplicationRegistryRevision.Compute(applications)) return false;
        await using var activeCommand = new MySqlCommand("""
            SELECT activity_id, application_id, details, detected_at FROM abnormal_activities
            WHERE computer_unit_id = @unit AND activity_type = 'FlaggedApplication' AND ended_at IS NULL FOR UPDATE
            """, connection, transaction);
        activeCommand.Parameters.AddWithValue("@unit", observation.UnitId.ToString());
        var active = new List<AbnormalActivity>();
        await using (var reader = await activeCommand.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                active.Add(new(reader.GetGuid(0), observation.UnitId, AbnormalActivityType.FlaggedApplication,
                    reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), Utc(reader.GetDateTime(3)), null));
        var changed = false;
        foreach (var activity in FlaggedApplicationDetection.Changes(observation, applications, active))
        {
            await using var command = new MySqlCommand(!activity.EndedAt.HasValue ? """
                INSERT INTO abnormal_activities (activity_id, computer_unit_id, activity_type, application_id, details, detected_at)
                VALUES (@id, @unit, 'FlaggedApplication', @app, @details, @at)
                """ : "UPDATE abnormal_activities SET ended_at = @at WHERE activity_id = @id AND ended_at IS NULL", connection, transaction);
            command.Parameters.AddWithValue("@id", activity.Id.ToString());
            command.Parameters.AddWithValue("@unit", observation.UnitId.ToString());
            command.Parameters.AddWithValue("@app", (object?)activity.ApplicationId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("@details", activity.Details);
            command.Parameters.AddWithValue("@at", activity.EndedAt ?? activity.DetectedAt);
            if (await command.ExecuteNonQueryAsync(ct) > 0) changed = true;
        }
        await using var watermark = new MySqlCommand("""
            UPDATE computer_units SET last_activity_scan_at = @at, last_activity_scan_id = @request WHERE computer_unit_id = @unit
            """, connection, transaction);
        watermark.Parameters.AddWithValue("@unit", observation.UnitId.ToString());
        watermark.Parameters.AddWithValue("@at", requested);
        watermark.Parameters.AddWithValue("@request", observation.RequestId.ToString());
        await watermark.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return changed;
    }

    public async Task<AbnormalActivityPage> GetPageAsync(AbnormalActivityQuery query, IReadOnlyCollection<Guid> allowedUnits, CancellationToken ct = default)
    {
        if (allowedUnits.Count == 0) return new([], false);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand { Connection = connection };
        var parameters = allowedUnits.Select((id, index) =>
        {
            var parameter = "@unit" + index;
            command.Parameters.AddWithValue(parameter, id.ToString());
            return parameter;
        });
        command.CommandText = """
            SELECT a.activity_id, a.computer_unit_id, a.activity_type, a.application_id, a.details, a.detected_at, a.ended_at,
                u.unit_name, u.laboratory_id, l.lab_name
            FROM abnormal_activities a INNER JOIN computer_units u ON u.computer_unit_id = a.computer_unit_id
            INNER JOIN laboratories l ON l.lab_id = u.laboratory_id
            WHERE u.deleted_at IS NULL
            """ + " AND a.computer_unit_id IN (" + string.Join(',', parameters) + ")" + """

                AND (@active = FALSE OR a.ended_at IS NULL)
                AND (@lab IS NULL OR u.laboratory_id = @lab)
                AND (@type IS NULL OR a.activity_type = @type)
                AND (@from IS NULL OR a.detected_at >= @from) AND (@to IS NULL OR a.detected_at < @to)
            ORDER BY a.detected_at DESC, a.activity_id DESC LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@active", query.ActiveOnly);
        command.Parameters.AddWithValue("@lab", (object?)query.LaboratoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@type", (object?)query.Type?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@from", (object?)query.FromUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("@to", (object?)query.ToUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("@offset", Math.Max(0, query.Offset));
        command.Parameters.AddWithValue("@limit", AbnormalActivityService.PageSize + 1);
        var rows = new List<AbnormalActivityRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(new(reader.GetGuid(0), reader.GetGuid(1), Enum.Parse<AbnormalActivityType>(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4), Utc(reader.GetDateTime(5)),
                    reader.IsDBNull(6) ? null : Utc(reader.GetDateTime(6))), reader.GetString(7), reader.GetInt32(8), reader.GetString(9)));
        return new(rows.Take(AbnormalActivityService.PageSize).ToArray(), rows.Count > AbnormalActivityService.PageSize);
    }

    public async Task<int> DeleteEndedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("DELETE FROM abnormal_activities WHERE ended_at IS NOT NULL AND ended_at < @cutoff", connection);
        command.Parameters.AddWithValue("@cutoff", cutoffUtc);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static DateTime Utc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc);
}
