using CLMS.Domain.Common;
using CLMS.Domain.Enums;

namespace CLMS.Domain.Entities.Inventory;

public class EquipmentAssignment : AuditableEntity
{
    public readonly Guid EquipmentId;
    public readonly Guid? ComputerUnitId;
    public readonly int? LaboratoryId;
    public AssignmentStatus Status { get; set; }
    public AssignmentEndReason? EndReason { get; set; }
    public string? Remarks { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public EquipmentAssignment(
        Guid id,
        Guid equipmentId,
        Guid? computerUnitId,
        int? laboratoryId,
        DateTime assignedAt)
    {
        if (computerUnitId is null == laboratoryId is null)
        {
            throw new ArgumentException("Exactly one of computerUnitId or laboratoryId must be set.");
        }

        _id = id;
        EquipmentId = equipmentId;
        ComputerUnitId = computerUnitId;
        LaboratoryId = laboratoryId;
        Status = AssignmentStatus.Active;
        AssignedAt = assignedAt;
    }

    public void End(AssignmentEndReason reason, string? remarks, DateTime endedAt)
    {
        Status = AssignmentStatus.Ended;
        EndReason = reason;
        Remarks = remarks;
        EndedAt = endedAt;
    }
}
