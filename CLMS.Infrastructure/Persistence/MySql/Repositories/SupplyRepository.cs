using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class SupplyRepository(MySqlConnectionFactory connectionFactory) : ISupplyRepository
{
    public async Task<int> CreateAsync(Supply supply)
    {
        const string sql = "CALL sp_supply_create(@Name, @Category, @Unit, @Status, @Remarks);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Name", supply.Name);
        command.Parameters.AddWithValue("@Category", supply.Category);
        command.Parameters.AddWithValue("@Unit", supply.Unit);
        command.Parameters.AddWithValue("@Status", ToDbStatus(supply.Status));
        command.Parameters.AddWithValue("@Remarks", (object?)supply.Remarks ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task<IEnumerable<SupplyDTO>> GetAllAsync()
    {
        var supplies = new List<SupplyDTO>();

        const string sql = "CALL sp_supply_get_all();";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            supplies.Add(MapSupply(reader));
        }

        return supplies;
    }

    public async Task<SupplyDTO?> GetByIdAsync(int id)
    {
        const string sql = "CALL sp_supply_get_by_id(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapSupply(reader);
    }

    public async Task UpdateAsync(Supply supply)
    {
        const string sql = "CALL sp_supply_update(@Name, @Category, @Unit, @Status, @Remarks, @Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", supply.Id);
        command.Parameters.AddWithValue("@Name", supply.Name);
        command.Parameters.AddWithValue("@Category", supply.Category);
        command.Parameters.AddWithValue("@Unit", supply.Unit);
        command.Parameters.AddWithValue("@Status", ToDbStatus(supply.Status));
        command.Parameters.AddWithValue("@Remarks", (object?)supply.Remarks ?? DBNull.Value);

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task UpdateStatusAsync(int id, SupplyStatus status)
    {
        const string sql = "CALL sp_supply_update_status(@Status, @Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Status", ToDbStatus(status));

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "CALL sp_supply_delete(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteAffectedRowsAsync();
    }

    private static SupplyDTO MapSupply(MySqlDataReader reader)
    {
        return new SupplyDTO
        {
            Id = reader.GetInt32("supply_id"),
            Name = reader.GetString("name"),
            Category = reader.GetString("category"),
            Unit = reader.GetString("unit"),
            Status = reader.GetString("status"),
            Remarks = reader.IsDBNull(reader.GetOrdinal("remarks")) ? null : reader.GetString("remarks")
        };
    }

    private static string ToDbStatus(SupplyStatus status) => status switch
    {
        SupplyStatus.FullyStocked => "fully_stocked",
        SupplyStatus.InStock => "in_stock",
        SupplyStatus.LowStock => "low_stock",
        SupplyStatus.OutOfStock => "out_of_stock",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
