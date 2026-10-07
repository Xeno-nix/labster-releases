using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Users;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class GuestRepository(MySqlConnectionFactory connectionFactory) : IGuestRepository
{
    public async Task<int> CreateAsync(Guest guest)
    {
        const string sql = """
            INSERT INTO guests
            (
                guest_name,
                organization,
                contact_number,
                purpose
            )
            VALUES
            (
                @GuestName,
                @Organization,
                @ContactNumber,
                @Purpose
            );

            SELECT LAST_INSERT_ID();
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@GuestName", guest.GuestName);
        command.Parameters.AddWithValue("@Organization", (object?)guest.Organization ?? DBNull.Value);
        command.Parameters.AddWithValue("@ContactNumber", (object?)guest.ContactNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@Purpose", (object?)guest.Purpose ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync();

        var id = Convert.ToInt32(result);
        guest.AssignId(id);
        return id;
    }

    public async Task<Guest?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT
                guest_id,
                guest_name,
                organization,
                contact_number,
                purpose
            FROM guests
            WHERE guest_id = @Id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapGuest(reader);
    }

    private static Guest MapGuest(MySqlDataReader reader)
    {
        var purposeOrdinal = reader.GetOrdinal("purpose");
        var guest = new Guest(
            reader.GetString("guest_name"),
            reader.IsDBNull(purposeOrdinal) ? null : reader.GetString(purposeOrdinal));

        guest.AssignId(reader.GetInt32("guest_id"));

        var orgOrdinal = reader.GetOrdinal("organization");
        if (!reader.IsDBNull(orgOrdinal))
            guest.Organization = reader.GetString(orgOrdinal);

        var contactOrdinal = reader.GetOrdinal("contact_number");
        if (!reader.IsDBNull(contactOrdinal))
            guest.ContactNumber = reader.GetString(contactOrdinal);

        return guest;
    }
}
