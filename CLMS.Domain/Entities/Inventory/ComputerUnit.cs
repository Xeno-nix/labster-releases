using CLMS.Domain.Common;

namespace CLMS.Domain.Entities.Inventory;

public class ComputerUnit : AuditableEntity
{
    public string UnitName { get; set; }
    public byte UnitNumber { get; set; }
    public readonly int LaboratoryId;

    public ComputerUnit(Guid id, string unitName, byte unitNumber, int laboratoryId)
    {
        UnitName = unitName;
        UnitNumber = unitNumber;
        LaboratoryId = laboratoryId;
        Id = id;
    }
}
