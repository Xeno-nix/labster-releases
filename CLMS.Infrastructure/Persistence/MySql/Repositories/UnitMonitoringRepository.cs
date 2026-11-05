using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public sealed class UnitMonitoringRepository(MySqlConnectionFactory connectionFactory) : IUnitMonitoringRepository
{
    public async Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Select one open session per PC, including legacy rows, without duplicating cards.
        const string sql = """
            SELECT u.computer_unit_id, u.unit_name, u.unit_number, u.laboratory_id,
                   l.lab_name, u.serial_number, a.ip_address, a.last_heartbeat,
                   a.is_online, s.time_in AS session_started,
                   CASE WHEN s.student_id IS NOT NULL
                        THEN NULLIF(TRIM(CONCAT_WS(' ', st.first_name, st.last_name)), '')
                        ELSE g.guest_name END AS session_user_name
            FROM computer_units u
            INNER JOIN laboratories l ON l.lab_id = u.laboratory_id
            LEFT JOIN unit_availability a ON a.computer_unit_id = u.computer_unit_id
            LEFT JOIN unit_utilization_logs s ON s.unit_utilization_log_id = (
                SELECT latest.unit_utilization_log_id
                FROM unit_utilization_logs latest
                WHERE latest.computer_unit_id = u.computer_unit_id AND latest.time_out IS NULL
                ORDER BY latest.time_in DESC, latest.unit_utilization_log_id DESC
                LIMIT 1
            )
            LEFT JOIN students st ON st.student_id = s.student_id
            LEFT JOIN guests g ON g.guest_id = s.guest_id
            WHERE u.deleted_at IS NULL
            ORDER BY l.lab_name, u.unit_number, u.unit_name;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var units = new List<UnitMonitoringDTO>();
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(new UnitMonitoringDTO
            {
                Id = reader.GetGuid("computer_unit_id"),
                Name = reader.GetString("unit_name"),
                Number = reader.GetByte("unit_number"),
                LaboratoryId = reader.GetInt32("laboratory_id"),
                Laboratory = reader.GetString("lab_name"),
                SerialNumber = ReadText(reader, "serial_number"),
                IpAddress = ReadText(reader, "ip_address"),
                LastHeartbeatUtc = ReadUtc(reader, "last_heartbeat"),
                IsOnline = !reader.IsDBNull(reader.GetOrdinal("is_online")) && reader.GetBoolean("is_online"),
                SessionStartedUtc = ReadUtc(reader, "session_started"),
                CurrentUser = ReadText(reader, "session_user_name")
            });
        }
        return units;
    }

    private static string? ReadText(MySqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(column);

    private static DateTime? ReadUtc(MySqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null
            : DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc);
}
