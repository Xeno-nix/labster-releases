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
        const string sql = """
            INSERT INTO computer_units
            (
                computer_unit_id,
                unit_name,
                unit_number,
                laboratory_id,
                created_at,
                last_modified_at
            )
            VALUES
            (
                @Id,
                @UnitName,
                @UnitNumber,
                @LaboratoryId,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            );
            """;

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", computerUnit.Id.ToString());
        command.Parameters.AddWithValue("@UnitName", computerUnit.UnitName);
        command.Parameters.AddWithValue("@UnitNumber", computerUnit.UnitNumber);
        command.Parameters.AddWithValue("@LaboratoryId", computerUnit.LaboratoryId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<ComputerUnitDTO>> GetAllAsync()
    {
        var units = new List<ComputerUnitDTO>();

        const string sql = """
            SELECT
                cu.computer_unit_id,
                cu.unit_name,
                cu.unit_number,
                cu.laboratory_id,
                l.lab_name
            FROM computer_units cu
            INNER JOIN laboratories l
                ON cu.laboratory_id = l.lab_id
            WHERE cu.deleted_at IS NULL
            ORDER BY l.lab_name, cu.unit_number;
            """;

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
        const string sql = """
            SELECT
                cu.computer_unit_id,
                cu.unit_name,
                cu.unit_number,
                cu.laboratory_id,
                l.lab_name
            FROM computer_units cu
            INNER JOIN laboratories l
                ON cu.laboratory_id = l.lab_id
            WHERE cu.computer_unit_id = @Id
              AND cu.deleted_at IS NULL;
            """;

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
        const string sql = """
            SELECT COUNT(*)
            FROM computer_units
            WHERE laboratory_id = @LaboratoryId
              AND unit_number = @UnitNumber
              AND deleted_at IS NULL
              AND (@ExcludingId IS NULL OR computer_unit_id <> @ExcludingId);
            """;

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
        await using var command = new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM computer_units WHERE laboratory_id=@Lab AND unit_number=@Number
              AND (@Excluded IS NULL OR computer_unit_id<>@Excluded))
            """, connection);
        command.Parameters.AddWithValue("@Lab", laboratoryId);
        command.Parameters.AddWithValue("@Number", unitNumber);
        command.Parameters.AddWithValue("@Excluded", excludingId is null ? DBNull.Value : excludingId.Value.ToString());
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(ComputerUnit computerUnit)
    {
        const string sql = """
            UPDATE computer_units
            SET
                unit_name = @UnitName,
                unit_number = @UnitNumber,
                laboratory_id = @LaboratoryId,
                last_modified_at = UTC_TIMESTAMP(6)
            WHERE computer_unit_id = @Id
              AND deleted_at IS NULL
              AND (registered_at IS NULL OR (laboratory_id=@LaboratoryId AND unit_number=@UnitNumber));
            """;

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", computerUnit.Id.ToString());
        command.Parameters.AddWithValue("@UnitName", computerUnit.UnitName);
        command.Parameters.AddWithValue("@UnitNumber", computerUnit.UnitNumber);
        command.Parameters.AddWithValue("@LaboratoryId", computerUnit.LaboratoryId);

        if (await command.ExecuteNonQueryAsync() == 0)
            throw new InvalidOperationException("Registered assignments cannot be changed. Use the registry deletion and approval workflow.");
    }

    public async Task DeleteAsync(Guid id)
    {
        await new CLMS.Infrastructure.Persistence.MySql.Repositories.UnitRegistryRepository(connectionFactory)
            .TransactionAsync(new(CLMS.Application.UnitRegistry.RegistryLockKind.Unit, id.ToString()), async tx =>
            {
                var unit = await tx.UnitAsync(id);
                if (unit is null) return false;
                if (!unit.DeletedAt.HasValue)
                {
                    unit.DeletedAt = unit.LastModifiedAt = tx.Now;
                    unit.Enabled = false;
                    unit.CredentialGeneration++;
                    await tx.SaveUnitAsync(unit, false);
                }
                await tx.RevokeCredentialsAsync(id);
                await tx.SetOfflineAsync(id);
                return true;
            });
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
