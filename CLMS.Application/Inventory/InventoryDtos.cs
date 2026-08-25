namespace CLMS.Application.Inventory;

public sealed record InventoryEquipmentDto(
    int EquipmentId,
    string EquipmentNo,
    string? SerialNo,
    string EquipmentName,
    string? Brand,
    string? ModelNumber,
    string EquipmentType,
    int LabId,
    string LabName,
    string EquipmentStatus,
    int Quantity,
    bool PreventiveMaintenanceDone,
    bool CalibrationDone,
    string? Remarks);

public sealed record InventorySupplyDto(
    int SupplyId,
    string SupplyName,
    string Category,
    string? Unit,
    string Status,
    string? Remarks);

public sealed record InventoryLabDto(int LabId, string LabName);
