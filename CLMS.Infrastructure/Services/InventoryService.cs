using System.Data;
using CLMS.Application.Interfaces;
using CLMS.Application.Inventory;
using Dapper;
using MySqlConnector;

namespace CLMS.Infrastructure.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public InventoryService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<InventoryEquipmentDto>> GetEquipmentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT e.id AS EquipmentId,
                       e.equipment_no AS EquipmentNo,
                       e.serial_number AS SerialNo,
                       e.equipment_name AS EquipmentName,
                       e.brand AS Brand,
                       e.model_number AS ModelNumber,
                       e.equipment_type AS EquipmentType,
                       e.laboratory_id AS LabId,
                       COALESCE(l.name, 'Unassigned') AS LabName,
                       e.status AS EquipmentStatus,
                       COALESCE(e.quantity, 1) AS Quantity,
                       e.preventive_maintenance_done AS PreventiveMaintenanceDone,
                       e.calibration_done AS CalibrationDone,
                       e.remarks AS Remarks
                FROM equipment e
                LEFT JOIN laboratories l ON l.id = e.laboratory_id
                ORDER BY e.id ASC;";

            var rows = await connection.QueryAsync<InventoryEquipmentDto>(sql);
            return rows.ToList();
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return GetFallbackEquipment();
        }
    }

    public async Task<IReadOnlyList<InventorySupplyDto>> GetSuppliesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT s.id AS SupplyId,
                       s.name AS SupplyName,
                       s.category AS Category,
                       s.unit AS Unit,
                       s.status AS Status,
                       s.remarks AS Remarks
                FROM supplies s
                ORDER BY s.id ASC;";

            var rows = await connection.QueryAsync<InventorySupplyDto>(sql);
            return rows.ToList();
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return GetFallbackSupplies();
        }
    }

    public async Task<IReadOnlyList<InventoryLabDto>> GetLabsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT id AS LabId, name AS LabName
                FROM laboratories
                ORDER BY name ASC;";

            var rows = await connection.QueryAsync<InventoryLabDto>(sql);
            return rows.ToList();
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return GetFallbackLabs();
        }
    }

    public async Task<InventoryEquipmentDto?> GetEquipmentByIdAsync(int equipmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT e.id AS EquipmentId,
                       e.equipment_no AS EquipmentNo,
                       e.serial_number AS SerialNo,
                       e.equipment_name AS EquipmentName,
                       e.brand AS Brand,
                       e.model_number AS ModelNumber,
                       e.equipment_type AS EquipmentType,
                       e.laboratory_id AS LabId,
                       COALESCE(l.name, 'Unassigned') AS LabName,
                       e.status AS EquipmentStatus,
                       COALESCE(e.quantity, 1) AS Quantity,
                       e.preventive_maintenance_done AS PreventiveMaintenanceDone,
                       e.calibration_done AS CalibrationDone,
                       e.remarks AS Remarks
                FROM equipment e
                LEFT JOIN laboratories l ON l.id = e.laboratory_id
                WHERE e.id = @EquipmentId;";

            return await connection.QuerySingleOrDefaultAsync<InventoryEquipmentDto>(sql, new { EquipmentId = equipmentId });
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return GetFallbackEquipment().FirstOrDefault(x => x.EquipmentId == equipmentId);
        }
    }

    public async Task CreateOrUpdateEquipmentAsync(InventoryEquipmentDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO equipment (
                    equipment_no, serial_number, equipment_name, brand, model_number,
                    equipment_type, laboratory_id, status, quantity,
                    preventive_maintenance_done, calibration_done, remarks,
                    created_at, last_modified_at
                ) VALUES (
                    @EquipmentNo, @SerialNo, @EquipmentName, @Brand, @ModelNumber,
                    @EquipmentType, @LabId, @EquipmentStatus, @Quantity,
                    @PreventiveMaintenanceDone, @CalibrationDone, @Remarks,
                    NOW(), NOW()
                )
                ON DUPLICATE KEY UPDATE
                    equipment_no = VALUES(equipment_no),
                    serial_number = VALUES(serial_number),
                    equipment_name = VALUES(equipment_name),
                    brand = VALUES(brand),
                    model_number = VALUES(model_number),
                    equipment_type = VALUES(equipment_type),
                    laboratory_id = VALUES(laboratory_id),
                    status = VALUES(status),
                    quantity = VALUES(quantity),
                    preventive_maintenance_done = VALUES(preventive_maintenance_done),
                    calibration_done = VALUES(calibration_done),
                    remarks = VALUES(remarks),
                    last_modified_at = NOW();";

            await connection.ExecuteAsync(sql, dto);
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            // The fallback data path keeps the UI usable even before the database schema is connected.
        }
    }

    public async Task DeleteEquipmentAsync(int equipmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync("DELETE FROM equipment WHERE id = @EquipmentId;", new { EquipmentId = equipmentId });
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            // No-op fallback when the database is not available yet.
        }
    }

    public async Task<InventorySupplyDto?> GetSupplyByIdAsync(int supplyId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT s.id AS SupplyId,
                       s.name AS SupplyName,
                       s.category AS Category,
                       s.unit AS Unit,
                       s.status AS Status,
                       s.remarks AS Remarks
                FROM supplies s
                WHERE s.id = @SupplyId;";

            return await connection.QuerySingleOrDefaultAsync<InventorySupplyDto>(sql, new { SupplyId = supplyId });
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return GetFallbackSupplies().FirstOrDefault(x => x.SupplyId == supplyId);
        }
    }

    public async Task CreateOrUpdateSupplyAsync(InventorySupplyDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO supplies (name, category, unit, status, remarks, created_at, last_modified_at)
                VALUES (@SupplyName, @Category, @Unit, @Status, @Remarks, NOW(), NOW())
                ON DUPLICATE KEY UPDATE
                    name = VALUES(name),
                    category = VALUES(category),
                    unit = VALUES(unit),
                    status = VALUES(status),
                    remarks = VALUES(remarks),
                    last_modified_at = NOW();";

            await connection.ExecuteAsync(sql, dto);
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            // No-op fallback when the database is not available yet.
        }
    }

    public async Task DeleteSupplyAsync(int supplyId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync("DELETE FROM supplies WHERE id = @SupplyId;", new { SupplyId = supplyId });
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            // No-op fallback when the database is not available yet.
        }
    }

    public async Task UpdateSupplyStatusAsync(int supplyId, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE supplies SET status = @Status, last_modified_at = NOW() WHERE id = @SupplyId;",
                new { SupplyId = supplyId, Status = status });
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            // No-op fallback when the database is not available yet.
        }
    }

    private static bool IsDatabaseUnavailable(Exception ex)
    {
        return ex is MySqlException or InvalidOperationException or TimeoutException;
    }

    private static IReadOnlyList<InventoryEquipmentDto> GetFallbackEquipment()
    {
        return new[]
        {
            new InventoryEquipmentDto(1, "EQ-001", "SN-2024-001", "Dell OptiPlex 7090", "Dell", "OptiPlex 7090", "computer_unit", 1, "Computer Lab 1", "available", 1, true, true, "Primary workstation"),
            new InventoryEquipmentDto(2, "EQ-002", "SN-2024-002", "HP Monitor 24 inch", "HP", "24f", "peripheral", 1, "Computer Lab 1", "in-use", 1, false, true, "Shared lab monitor"),
            new InventoryEquipmentDto(3, "EQ-003", "SN-2024-003", "Cisco Switch", "Cisco", "SG-350", "component", 2, "Networking Lab", "maintenance", 1, true, false, "Needs firmware upgrade")
        };
    }

    private static IReadOnlyList<InventorySupplyDto> GetFallbackSupplies()
    {
        return new[]
        {
            new InventorySupplyDto(1, "Bond Paper", "Office Supplies", "ream", "in_stock", "Used for printers"),
            new InventorySupplyDto(2, "Ink Cartridge", "Printing Supplies", "piece", "low_stock", "Reorder next week"),
            new InventorySupplyDto(3, "Cotton Swab", "Cleaning Supplies", "box", "fully_stocked", "For sanitation checks")
        };
    }

    private static IReadOnlyList<InventoryLabDto> GetFallbackLabs()
    {
        return new[]
        {
            new InventoryLabDto(1, "Computer Lab 1"),
            new InventoryLabDto(2, "Networking Lab"),
            new InventoryLabDto(3, "Engineering Lab")
        };
    }
}
