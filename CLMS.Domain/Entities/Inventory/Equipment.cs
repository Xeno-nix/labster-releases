using CLMS.Domain.Common;
using CLMS.Domain.Enums;

namespace CLMS.Domain.Entities.Inventory;

public class Equipment : AuditableEntity, IEquipment
{
    public string EquipmentNo { get; set; }
    public string? SerialNumber { get; set; }
    public string EquipmentName { get; set; }
    public string? Brand { get; set; }
    public string? ModelNumber { get; set; }
    public string EquipmentType { get; set; }
    public EquipmentStatus Status { get; set; }
    public bool NeedsPm { get; set; }
    public bool NeedsCalibration { get; set; }
    public string? Remarks { get; set; }

    public Equipment(
        Guid id,
        string equipmentNo,
        string? serialNumber,
        string equipmentName,
        string? brand,
        string? modelNumber,
        string equipmentType,
        EquipmentStatus status,
        bool needsPm,
        bool needsCalibration,
        string? remarks)
    {
        Id = id;
        EquipmentNo = equipmentNo;
        SerialNumber = serialNumber;
        EquipmentName = equipmentName;
        Brand = brand;
        ModelNumber = modelNumber;
        EquipmentType = equipmentType;
        Status = status;
        NeedsPm = needsPm;
        NeedsCalibration = needsCalibration;
        Remarks = remarks;
    }
}
