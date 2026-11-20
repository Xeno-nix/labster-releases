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

        const string sql = """
            SELECT u.computer_unit_id, u.unit_name, u.unit_number, u.laboratory_id,
                   l.lab_name, a.ip_address, a.last_heartbeat, a.is_online
            FROM computer_units u
            INNER JOIN laboratories l ON l.lab_id = u.laboratory_id
            LEFT JOIN unit_availability a ON a.computer_unit_id = u.computer_unit_id
            WHERE u.deleted_at IS NULL
            ORDER BY l.lab_name, u.unit_number, u.unit_name;
            """;

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
                IsOnline = !reader.IsDBNull(reader.GetOrdinal("is_online")) && reader.GetBoolean("is_online")
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
