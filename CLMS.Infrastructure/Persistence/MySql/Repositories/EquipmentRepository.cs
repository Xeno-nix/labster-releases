using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class EquipmentRepository(MySqlConnectionFactory connectionFactory) : IEquipmentRepository
{
    // ---------------------------------------------------------------
    // Equipment CRUD
    // ---------------------------------------------------------------

    public async Task CreateAsync(Equipment equipment)
    {
        const string sql = "CALL sp_equipment_create(@Id, @EquipmentNo, @SerialNumber, @EquipmentName, @Brand, @ModelNumber, @EquipmentType, @Status, @NeedsPm, @NeedsCalibration, @Remarks);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        AddEquipmentParameters(command, equipment);

        try
        {
            await command.ExecuteAffectedRowsAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("EQUIPMENT_NO_CONFLICT");
        }
    }

    public async Task<IEnumerable<EquipmentDTO>> GetAllAsync()
    {
        var equipment = new List<EquipmentDTO>();

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand("CALL sp_equipment_get_all();", connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            equipment.Add(MapEquipment(reader));
        }

        return equipment;
    }

    public async Task<EquipmentDTO?> GetByIdAsync(Guid id)
    {
        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            "CALL sp_equipment_get_by_id(@Id);",
            connection);

        command.Parameters.AddWithValue("@Id", id.ToString());

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapEquipment(reader);
    }

    public async Task UpdateAsync(Equipment equipment)
    {
        const string sql = "CALL sp_equipment_update(@SerialNumber, @EquipmentName, @Brand, @ModelNumber, @EquipmentType, @Status, @NeedsPm, @NeedsCalibration, @Remarks, @Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        AddEquipmentParameters(command, equipment);

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task<string> GetNextEquipmentNoAsync()
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_equipment_get_next_equipment_no();", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    public async Task DeleteAsync(Guid id)
    {
        const string sql = "CALL sp_equipment_delete(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id.ToString());

        await command.ExecuteAffectedRowsAsync();
    }

    // ---------------------------------------------------------------
    // Assignment history
    // ---------------------------------------------------------------

    public async Task CreateAssignmentAsync(EquipmentAssignment assignment)
    {
        const string sql = "CALL sp_equipment_create_assignment(@Id, @EquipmentId, @ComputerUnitId, @LaboratoryId, @Status, @AssignedAt);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", assignment.Id.ToString());
        command.Parameters.AddWithValue("@EquipmentId", assignment.EquipmentId.ToString());
        command.Parameters.AddWithValue(
            "@ComputerUnitId",
            assignment.ComputerUnitId is null ? DBNull.Value : assignment.ComputerUnitId.Value.ToString());
        command.Parameters.AddWithValue(
            "@LaboratoryId",
            assignment.LaboratoryId is null ? DBNull.Value : assignment.LaboratoryId.Value);
        command.Parameters.AddWithValue("@Status", ToDbAssignmentStatus(assignment.Status));
        command.Parameters.AddWithValue("@AssignedAt", assignment.AssignedAt);

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task<EquipmentAssignment?> GetActiveAssignmentAsync(Guid equipmentId)
    {
        const string sql = "CALL sp_equipment_get_active_assignment(@EquipmentId);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@EquipmentId", equipmentId.ToString());

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new EquipmentAssignment(
            id: ReadGuid(reader, "assignment_id"),
            equipmentId: ReadGuid(reader, "equipment_id"),
            computerUnitId: reader.IsDBNull(reader.GetOrdinal("computer_unit_id"))
                ? null
                : ReadGuid(reader, "computer_unit_id"),
            laboratoryId: reader.IsDBNull(reader.GetOrdinal("laboratory_id"))
                ? null
                : reader.GetInt32("laboratory_id"),
            assignedAt: reader.GetDateTime("assigned_at"));
    }

    public async Task<EquipmentAssignment?> GetAssignmentByIdAsync(Guid assignmentId)
    {
        const string sql = "CALL sp_equipment_get_assignment_by_id(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", assignmentId.ToString());

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        // The constructor always sets Status to Active, so rehydrate the real
        // persisted state (which may already be Ended) via the public setters.
        var assignment = new EquipmentAssignment(
            id: ReadGuid(reader, "assignment_id"),
            equipmentId: ReadGuid(reader, "equipment_id"),
            computerUnitId: reader.IsDBNull(reader.GetOrdinal("computer_unit_id"))
                ? null
                : ReadGuid(reader, "computer_unit_id"),
            laboratoryId: reader.IsDBNull(reader.GetOrdinal("laboratory_id"))
                ? null
                : reader.GetInt32("laboratory_id"),
            assignedAt: reader.GetDateTime("assigned_at"))
        {
            Status = FromDbAssignmentStatus(reader.GetString("status")),
            EndReason = reader.IsDBNull(reader.GetOrdinal("end_reason"))
                ? null
                : FromDbEndReason(reader.GetString("end_reason")),
            Remarks = reader.IsDBNull(reader.GetOrdinal("remarks")) ? null : reader.GetString("remarks"),
            EndedAt = reader.IsDBNull(reader.GetOrdinal("ended_at")) ? null : reader.GetDateTime("ended_at")
        };

        return assignment;
    }

    public async Task EndAssignmentAsync(EquipmentAssignment assignment)
    {
        const string sql = "CALL sp_equipment_end_assignment(@Status, @EndReason, @Remarks, @EndedAt, @Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", assignment.Id.ToString());
        command.Parameters.AddWithValue("@Status", ToDbAssignmentStatus(assignment.Status));
        command.Parameters.AddWithValue(
            "@EndReason",
            assignment.EndReason is null ? DBNull.Value : ToDbEndReason(assignment.EndReason.Value));
        command.Parameters.AddWithValue(
            "@Remarks",
            (object?)assignment.Remarks ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@EndedAt",
            (object?)assignment.EndedAt ?? DBNull.Value);

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentHistoryAsync(Guid equipmentId)
    {
        const string sql = "CALL sp_equipment_get_assignment_history(@EquipmentId);";

        var assignments = new List<EquipmentAssignmentDTO>();

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@EquipmentId", equipmentId.ToString());

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            assignments.Add(MapAssignment(reader));
        }

        return assignments;
    }

    public async Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentsForComputerUnitAsync(Guid computerUnitId)
    {
        const string sql = "CALL sp_equipment_get_assignments_for_computer_unit(@ComputerUnitId);";

        var assignments = new List<EquipmentAssignmentDTO>();

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@ComputerUnitId", computerUnitId.ToString());

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            assignments.Add(MapAssignment(reader));
        }

        return assignments;
    }

    // ---------------------------------------------------------------
    // Shared SQL / mapping / enum-string conversion
    // ---------------------------------------------------------------

    // NOTE: assumes at most one active assignment per equipment item at a time,
    // enforced by EquipmentService before calling CreateAssignmentAsync — not by
    // a DB constraint. If that invariant is ever violated the LEFT JOIN below
    // will duplicate the equipment row per active assignment found.


    private static void AddEquipmentParameters(MySqlCommand command, Equipment equipment)
    {
        command.Parameters.AddWithValue("@Id", equipment.Id.ToString());
        command.Parameters.AddWithValue("@EquipmentNo", equipment.EquipmentNo);
        command.Parameters.AddWithValue("@SerialNumber", (object?)equipment.SerialNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@EquipmentName", equipment.EquipmentName);
        command.Parameters.AddWithValue("@Brand", (object?)equipment.Brand ?? DBNull.Value);
        command.Parameters.AddWithValue("@ModelNumber", (object?)equipment.ModelNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@EquipmentType", equipment.EquipmentType);
        command.Parameters.AddWithValue("@Status", ToDbEquipmentStatus(equipment.Status));
        command.Parameters.AddWithValue("@NeedsPm", equipment.NeedsPm);
        command.Parameters.AddWithValue("@NeedsCalibration", equipment.NeedsCalibration);
        command.Parameters.AddWithValue("@Remarks", (object?)equipment.Remarks ?? DBNull.Value);
    }

    private static Guid ReadGuid(MySqlDataReader reader, string columnName)
    {
        var value = reader.GetValue(reader.GetOrdinal(columnName));
        return value is Guid guid ? guid : Guid.Parse((string)value);
    }

    private static EquipmentDTO MapEquipment(MySqlDataReader reader)
    {
        return new EquipmentDTO
        {
            Id = ReadGuid(reader, "equipment_id"),
            EquipmentNo = reader.GetString("equipment_no"),
            SerialNumber = reader.IsDBNull(reader.GetOrdinal("serial_number")) ? null : reader.GetString("serial_number"),
            EquipmentName = reader.GetString("equipment_name"),
            Brand = reader.IsDBNull(reader.GetOrdinal("brand")) ? null : reader.GetString("brand"),
            ModelNumber = reader.IsDBNull(reader.GetOrdinal("model_number")) ? null : reader.GetString("model_number"),
            EquipmentType = reader.GetString("equipment_type"),
            Status = reader.GetString("status"),
            NeedsPm = reader.GetBoolean("needs_pm"),
            NeedsCalibration = reader.GetBoolean("needs_calibration"),
            Remarks = reader.IsDBNull(reader.GetOrdinal("remarks")) ? null : reader.GetString("remarks"),
            CurrentComputerUnitId = reader.IsDBNull(reader.GetOrdinal("current_computer_unit_id"))
                ? null
                : ReadGuid(reader, "current_computer_unit_id"),
            CurrentComputerUnitName = reader.IsDBNull(reader.GetOrdinal("current_computer_unit_name"))
                ? null
                : reader.GetString("current_computer_unit_name"),
            CurrentLaboratoryId = reader.IsDBNull(reader.GetOrdinal("current_laboratory_id"))
                ? null
                : reader.GetInt32("current_laboratory_id"),
            CurrentLaboratoryName = reader.IsDBNull(reader.GetOrdinal("current_laboratory_name"))
                ? null
                : reader.GetString("current_laboratory_name")
        };
    }

    private static EquipmentAssignmentDTO MapAssignment(MySqlDataReader reader)
    {
        var laboratoryId = reader.IsDBNull(reader.GetOrdinal("laboratory_id"))
            ? (int?)null
            : reader.GetInt32("laboratory_id");

        // Location name comes from whichever side of the assignment is set:
        // direct lab assignment (direct_laboratory_name) or via a computer unit (unit_laboratory_name).
        var laboratoryName = reader.IsDBNull(reader.GetOrdinal("direct_laboratory_name"))
            ? (reader.IsDBNull(reader.GetOrdinal("unit_laboratory_name")) ? null : reader.GetString("unit_laboratory_name"))
            : reader.GetString("direct_laboratory_name");

        return new EquipmentAssignmentDTO
        {
            Id = ReadGuid(reader, "assignment_id"),
            EquipmentId = ReadGuid(reader, "equipment_id"),
            EquipmentName = reader.GetString("equipment_name"),
            ComputerUnitId = reader.IsDBNull(reader.GetOrdinal("computer_unit_id"))
                ? null
                : ReadGuid(reader, "computer_unit_id"),
            ComputerUnitName = reader.IsDBNull(reader.GetOrdinal("computer_unit_name"))
                ? null
                : reader.GetString("computer_unit_name"),
            LaboratoryId = laboratoryId,
            LaboratoryName = laboratoryName,
            Status = reader.GetString("status"),
            EndReason = reader.IsDBNull(reader.GetOrdinal("end_reason")) ? null : reader.GetString("end_reason"),
            Remarks = reader.IsDBNull(reader.GetOrdinal("remarks")) ? null : reader.GetString("remarks"),
            AssignedAt = reader.GetDateTime("assigned_at"),
            EndedAt = reader.IsDBNull(reader.GetOrdinal("ended_at")) ? null : reader.GetDateTime("ended_at")
        };
    }

    private static string ToDbEquipmentStatus(EquipmentStatus status) => status switch
    {
        EquipmentStatus.Available => "available",
        EquipmentStatus.InUse => "in_use",
        EquipmentStatus.Maintenance => "maintenance",
        EquipmentStatus.Damaged => "damaged",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string ToDbAssignmentStatus(AssignmentStatus status) => status switch
    {
        AssignmentStatus.Active => "active",
        AssignmentStatus.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static AssignmentStatus FromDbAssignmentStatus(string status) => status switch
    {
        "active" => AssignmentStatus.Active,
        "ended" => AssignmentStatus.Ended,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unrecognized assignment status.")
    };

    private static AssignmentEndReason FromDbEndReason(string reason) => reason switch
    {
        "broken" => AssignmentEndReason.Broken,
        "replaced" => AssignmentEndReason.Replaced,
        "moved" => AssignmentEndReason.Moved,
        "returned" => AssignmentEndReason.Returned,
        "lost" => AssignmentEndReason.Lost,
        "other" => AssignmentEndReason.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unrecognized end reason.")
    };

    private static string ToDbEndReason(AssignmentEndReason reason) => reason switch
    {
        AssignmentEndReason.Broken => "broken",
        AssignmentEndReason.Replaced => "replaced",
        AssignmentEndReason.Moved => "moved",
        AssignmentEndReason.Returned => "returned",
        AssignmentEndReason.Lost => "lost",
        AssignmentEndReason.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };
}
