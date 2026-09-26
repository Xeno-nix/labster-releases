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
    /// Used to enforce uniqueness at the service layer instead of a DB constraint,
    /// since deleted_at makes a plain UNIQUE(laboratory_id, unit_number) unreliable.
    /// </summary>
    Task<bool> ExistsActiveAtLabAndNumberAsync(int laboratoryId, byte unitNumber, Guid? excludingId = null);

    Task UpdateAsync(ComputerUnit computerUnit);

    Task DeleteAsync(Guid id);
}
