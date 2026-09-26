namespace CLMS.Application.DTOs.Inventory;

public class AssignEquipmentDTO
{
    public Guid EquipmentId { get; set; }

    // Exactly one of these two must be set:
    // - ComputerUnitId, when the equipment is becoming part of a computer unit (e.g. a mouse -> Unit 1)
    // - LaboratoryId, when the equipment is assigned directly to a lab (e.g. a projector -> Comlab 1)
    public Guid? ComputerUnitId { get; set; }
    public int? LaboratoryId { get; set; }
}
