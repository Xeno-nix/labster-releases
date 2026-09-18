using CLMS.Application.DTOs.Inventory;

namespace CLMS.Application.Interfaces;

public interface IEquipmentService
{
    Task<Guid> CreateAsync(UpsertEquipmentDTO dto);

    Task<IEnumerable<EquipmentDTO>> GetAllAsync();

    Task<EquipmentDTO?> GetByIdAsync(Guid id);

    Task UpdateAsync(Guid id, UpsertEquipmentDTO dto);

    Task DeleteAsync(Guid id);

    /// <summary>
    /// Assigns equipment to a computer unit or directly to a laboratory.
    /// Throws if the equipment already has an active assignment — end it first.
    /// </summary>
    Task<Guid> AssignAsync(AssignEquipmentDTO dto);

    /// <summary>
    /// Ends the equipment's current active assignment with a reason (broken, replaced, moved, etc.).
    /// </summary>
    Task EndAssignmentAsync(Guid assignmentId, EndEquipmentAssignmentDTO dto);

    /// <summary>
    /// Same as EndAssignmentAsync, but looked up by equipment id instead of assignment id —
    /// for callers (like the equipment detail view) that only know "this equipment's current
    /// assignment," not its underlying assignment id.
    /// </summary>
    Task EndCurrentAssignmentAsync(Guid equipmentId, EndEquipmentAssignmentDTO dto);

    /// <summary>Full assignment history for one piece of equipment, most recent first.</summary>
    Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentHistoryAsync(Guid equipmentId);

    /// <summary>Equipment currently assigned to a given computer unit (e.g. what's plugged into Unit 1).</summary>
    Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentsForComputerUnitAsync(Guid computerUnitId);
}
