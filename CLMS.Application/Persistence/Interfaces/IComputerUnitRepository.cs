using CLMS.Application.DTOs.Inventory;
using CLMS.Domain.Entities.Inventory;

namespace CLMS.Application.Interfaces.Repositories;

public interface IComputerUnitRepository
{
    Task CreateAsync(ComputerUnit computerUnit);

    Task<IEnumerable<ComputerUnitDTO>> GetAllAsync();

    Task<ComputerUnitDTO?> GetByIdAsync(Guid id);

    /// <summary>
    /// Whether an active (non-deleted) unit already occupies this unit number in this lab.
    /// Registry uniqueness also includes deleted units, whose identities are reactivated.
    /// </summary>
    Task<bool> ExistsActiveAtLabAndNumberAsync(int laboratoryId, byte unitNumber, Guid? excludingId = null);
    Task<bool> ExistsAtLabAndNumberAsync(int laboratoryId, byte unitNumber, Guid? excludingId = null);

    Task UpdateAsync(ComputerUnit computerUnit);

    Task DeleteAsync(Guid id);
}
