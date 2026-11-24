using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public sealed class UnitMonitoringRepository(MySqlConnectionFactory connectionFactory) : IUnitMonitoringRepository
{
    public async Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Select one open session per PC, including legacy rows, without duplicating cards.
        const string sql = "CALL sp_unit_monitoring_get_all();";

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
