using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class LaboratoryRepository(MySqlConnectionFactory connectionFactory ) : ILaboratoryRepository
{
    public async Task<IEnumerable<Laboratory>> GetAllAsync()
    {
        var laboratories = new List<Laboratory>();

        const string sql = """
            SELECT
                lab_id,
                lab_name
            FROM laboratories
            ORDER BY lab_name;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            laboratories.Add(MapLaboratory(reader));
        }

        return laboratories;
    }

    public async Task<Laboratory?> GetByIdAsync(int id)
    {
      const string sql = """
            SELECT
                lab_id,
                lab_name    
            FROM laboratories
            WHERE lab_id = @Id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapLaboratory(reader);
    }
 

    private static Laboratory MapLaboratory(MySqlDataReader reader)
    {
        return new Laboratory
        {
            Id = reader.GetInt32("lab_id"),
            Name = reader.GetString("lab_name")
        };
    }

}


        
