using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class EquipmentRepository : IEquipmentRepository
{
    private readonly string _connectionString;

    public EquipmentRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    // ---------------------------------------------------------------
    // Equipment CRUD
    // ---------------------------------------------------------------

    public async Task CreateAsync(Equipment equipment)
    {
        const string sql = """
            INSERT INTO equipment
            (
                equipment_id,
                equipment_no,
                serial_number,
                equipment_name,
                brand,
                model_number,
                equipment_type,
                status,
                needs_pm,
                needs_calibration,
                remarks
            )
            VALUES
            (
                @Id,
                @EquipmentNo,
                @SerialNumber,
                @EquipmentName,
                @Brand,
                @ModelNumber,
                @EquipmentType,
                @Status,
                @NeedsPm,
                @NeedsCalibration,
                @Remarks
            );
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        AddEquipmentParameters(command, equipment);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<EquipmentDTO>> GetAllAsync()
    {
        var equipment = new List<EquipmentDTO>();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(EquipmentWithAssignmentSql + "\nWHERE e.deleted_at IS NULL ORDER BY e.equipment_no;", connection);

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
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            EquipmentWithAssignmentSql + "\nWHERE e.equipment_id = @Id AND e.deleted_at IS NULL;",
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
        const string sql = """
            UPDATE equipment
            SET
                equipment_no = @EquipmentNo,
                serial_number = @SerialNumber,
                equipment_name = @EquipmentName,
                brand = @Brand,
                model_number = @ModelNumber,
                equipment_type = @EquipmentType,
                status = @Status,
                needs_pm = @NeedsPm,
                needs_calibration = @NeedsCalibration,
                remarks = @Remarks
            WHERE equipment_id = @Id
              AND deleted_at IS NULL;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        AddEquipmentParameters(command, equipment);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        const string sql = """
            UPDATE equipment
            SET
                deleted_at = NOW()
            WHERE equipment_id = @Id
              AND deleted_at IS NULL;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id.ToString());

        await command.ExecuteNonQueryAsync();
    }

    // ---------------------------------------------------------------
    // Assignment history
    // ---------------------------------------------------------------

    public async Task CreateAssignmentAsync(EquipmentAssignment assignment)
    {
        const string sql = """
            INSERT INTO equipment_assignments
            (
                assignment_id,
                equipment_id,
                computer_unit_id,
                laboratory_id,
                status,
                assigned_at
            )
            VALUES
            (
                @Id,
                @EquipmentId,
                @ComputerUnitId,
                @LaboratoryId,
                @Status,
                @AssignedAt
            );
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

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

        await command.ExecuteNonQueryAsync();
    }

    public async Task<EquipmentAssignment?> GetActiveAssignmentAsync(Guid equipmentId)
    {
        const string sql = """
            SELECT
                assignment_id,
                equipment_id,
                computer_unit_id,
                laboratory_id,
                assigned_at
            FROM equipment_assignments
            WHERE equipment_id = @EquipmentId
              AND status = 'active'
            LIMIT 1;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

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
        const string sql = """
            SELECT
                assignment_id,
                equipment_id,
                computer_unit_id,
                laboratory_id,
                status,
                end_reason,
                remarks,
                assigned_at,
                ended_at
            FROM equipment_assignments
            WHERE assignment_id = @Id;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

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
        const string sql = """
            UPDATE equipment_assignments
            SET
                status = @Status,
                end_reason = @EndReason,
                remarks = @Remarks,
                ended_at = @EndedAt
            WHERE assignment_id = @Id
              AND status = 'active';
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

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

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentHistoryAsync(Guid equipmentId)
    {
        const string sql = AssignmentSelectSql + """
            WHERE ea.equipment_id = @EquipmentId
            ORDER BY ea.assigned_at DESC;
            """;

        var assignments = new List<EquipmentAssignmentDTO>();

        await using var connection =
            new MySqlConnection(_connectionString);

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
        const string sql = AssignmentSelectSql + """
            WHERE ea.computer_unit_id = @ComputerUnitId
              AND ea.status = 'active'
            ORDER BY ea.assigned_at DESC;
            """;

        var assignments = new List<EquipmentAssignmentDTO>();

        await using var connection =
            new MySqlConnection(_connectionString);

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
    private const string EquipmentWithAssignmentSql = """
        SELECT
            e.equipment_id,
            e.equipment_no,
            e.serial_number,
            e.equipment_name,
            e.brand,
            e.model_number,
            e.equipment_type,
            e.status,
            e.needs_pm,
            e.needs_calibration,
            e.remarks,
            ea.computer_unit_id AS current_computer_unit_id,
            cu.unit_name AS current_computer_unit_name,
            COALESCE(cu.laboratory_id, ea.laboratory_id) AS current_laboratory_id,
            COALESCE(cu_lab.lab_name, direct_lab.lab_name) AS current_laboratory_name
        FROM equipment e
        LEFT JOIN equipment_assignments ea
            ON ea.equipment_id = e.equipment_id AND ea.status = 'active'
        LEFT JOIN computer_units cu
            ON ea.computer_unit_id = cu.computer_unit_id
        LEFT JOIN laboratories cu_lab
            ON cu.laboratory_id = cu_lab.lab_id
        LEFT JOIN laboratories direct_lab
            ON ea.laboratory_id = direct_lab.lab_id
        """;

    private const string AssignmentSelectSql = """
        SELECT
            ea.assignment_id,
            ea.equipment_id,
            e.equipment_name,
            ea.computer_unit_id,
            cu.unit_name AS computer_unit_name,
            ea.laboratory_id,
            l.lab_name AS direct_laboratory_name,
            cu_lab.lab_name AS unit_laboratory_name,
            ea.status,
            ea.end_reason,
            ea.remarks,
            ea.assigned_at,
            ea.ended_at
        FROM equipment_assignments ea
        INNER JOIN equipment e
            ON ea.equipment_id = e.equipment_id
        LEFT JOIN computer_units cu
            ON ea.computer_unit_id = cu.computer_unit_id
        LEFT JOIN laboratories l
            ON ea.laboratory_id = l.lab_id
        LEFT JOIN laboratories cu_lab
            ON cu.laboratory_id = cu_lab.lab_id
        """;

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
