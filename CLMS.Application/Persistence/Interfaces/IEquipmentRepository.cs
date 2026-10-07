using CLMS.Application.DTOs.Inventory;
using CLMS.Domain.Entities.Inventory;

namespace CLMS.Application.Interfaces.Repositories;

public interface IEquipmentRepository
{
    Task CreateAsync(Equipment equipment);

    Task<IEnumerable<EquipmentDTO>> GetAllAsync();

    Task<EquipmentDTO?> GetByIdAsync(Guid id);

    Task UpdateAsync(Equipment equipment);

    Task DeleteAsync(Guid id);

    Task CreateAssignmentAsync(EquipmentAssignment assignment);

    /// <summary>The equipment's current active assignment, if any. Null when unassigned.</summary>
    Task<EquipmentAssignment?> GetActiveAssignmentAsync(Guid equipmentId);

    /// <summary>A specific assignment record by its own id, active or already ended. Null if not found.</summary>
    Task<EquipmentAssignment?> GetAssignmentByIdAsync(Guid assignmentId);

    Task EndAssignmentAsync(EquipmentAssignment assignment);

    /// <summary>Full assignment history for one piece of equipment, most recent first.</summary>
    Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentHistoryAsync(Guid equipmentId);

    /// <summary>Equipment currently assigned to a given computer unit.</summary>
    Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentsForComputerUnitAsync(Guid computerUnitId);

    // method to compute the next number
    Task<string> GetNextEquipmentNoAsync();
}
