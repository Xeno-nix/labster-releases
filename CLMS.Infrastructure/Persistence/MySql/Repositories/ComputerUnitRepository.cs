using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class ComputerUnitRepository(MySqlConnectionFactory connectionFactory) : IComputerUnitRepository
{

    public async Task CreateAsync(ComputerUnit computerUnit)
    {
        const string sql = "CALL sp_computer_unit_create(@Id, @UnitName, @UnitNumber, @LaboratoryId);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", computerUnit.Id.ToString());
        command.Parameters.AddWithValue("@UnitName", computerUnit.UnitName);
        command.Parameters.AddWithValue("@UnitNumber", computerUnit.UnitNumber);
        command.Parameters.AddWithValue("@LaboratoryId", computerUnit.LaboratoryId);

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task<IEnumerable<ComputerUnitDTO>> GetAllAsync()
    {
        var units = new List<ComputerUnitDTO>();

        const string sql = "CALL sp_computer_unit_get_all();";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            units.Add(MapComputerUnit(reader));
        }

        return units;
    }

    public async Task<ComputerUnitDTO?> GetByIdAsync(Guid id)
    {
        const string sql = "CALL sp_computer_unit_get_by_id(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id.ToString());

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapComputerUnit(reader);
    }

    public async Task<bool> ExistsActiveAtLabAndNumberAsync(
        int laboratoryId,
        byte unitNumber,
        Guid? excludingId = null)
    {
        const string sql = "CALL sp_computer_unit_exists_active_at_lab_and_number(@LaboratoryId, @UnitNumber, @ExcludingId);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@LaboratoryId", laboratoryId);
        command.Parameters.AddWithValue("@UnitNumber", unitNumber);
        command.Parameters.AddWithValue(
            "@ExcludingId",
            excludingId is null ? DBNull.Value : excludingId.Value.ToString());

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result) > 0;
    }

    public async Task<bool> ExistsAtLabAndNumberAsync(int laboratoryId, byte unitNumber, Guid? excludingId = null)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_computer_unit_exists_at_lab_and_number(@p0, @p1, @p2);", connection);
        command.Parameters.AddWithValue("p0", laboratoryId);
        command.Parameters.AddWithValue("p1", unitNumber);
        command.Parameters.AddWithValue("p2", (object?)excludingId?.ToString() ?? DBNull.Value);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(ComputerUnit computerUnit)
    {
        const string sql = "CALL sp_computer_unit_update(@UnitName, @UnitNumber, @LaboratoryId, @Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", computerUnit.Id.ToString());
        command.Parameters.AddWithValue("@UnitName", computerUnit.UnitName);
        command.Parameters.AddWithValue("@UnitNumber", computerUnit.UnitNumber);
        command.Parameters.AddWithValue("@LaboratoryId", computerUnit.LaboratoryId);

        if (await command.ExecuteAffectedRowsAsync() == 0)
            throw new InvalidOperationException("Registered assignments cannot be changed. Use the registry deletion and approval workflow.");
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_computer_unit_delete(@p0);", connection);
        command.Parameters.AddWithValue("p0", id.ToString());
        try { await command.ExecuteAffectedRowsAsync(); }
        catch (MySqlException ex) when (ex.Number == 1644)
        { throw CLMS.Application.UnitRegistry.RegistryException.InvalidCredential(); }
    }

    private static ComputerUnitDTO MapComputerUnit(MySqlDataReader reader)
    {
        return new ComputerUnitDTO
        {
            Id = reader.GetGuid("computer_unit_id"),
            UnitName = reader.GetString("unit_name"),
            UnitNumber = reader.GetByte("unit_number"),
            LaboratoryId = reader.GetInt32("laboratory_id"),
            LaboratoryName = reader.GetString("lab_name")
        };
    }
}
