using CLMS.Application.DTOs.Inventory;

namespace CLMS.Application.Interfaces;

public interface IComputerUnitService
{
    Task<Guid> CreateAsync(UpsertComputerUnitDTO dto);

    Task<IEnumerable<ComputerUnitDTO>> GetAllAsync();

    Task<ComputerUnitDTO?> GetByIdAsync(Guid id);

    Task UpdateAsync(Guid id, UpsertComputerUnitDTO dto);

    Task DeleteAsync(Guid id);
}
