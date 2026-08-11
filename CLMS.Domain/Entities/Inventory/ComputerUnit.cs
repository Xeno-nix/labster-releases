using CLMS.Domain.Common;

namespace CLMS.Domain.Entities.Inventory;

public class ComputerUnit : AuditableEntity , IEquipment
{
    public string SerialNumber { get; set; }
    public string EquipmentName { get; set; }
    public byte UnitNumber { get; set; }
    public int LaboratoryId { get; set; }
    
    public ComputerUnit(Guid id)
    {
        _id = id;
    }
}
