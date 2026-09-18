using CLMS.Application.DTOs.Inventory;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;

namespace CLMS.Application.Interfaces.Repositories;

public interface ISupplyRepository
{
    Task<int> CreateAsync(Supply supply);

    Task<IEnumerable<SupplyDTO>> GetAllAsync();

    Task<SupplyDTO?> GetByIdAsync(int id);

    Task UpdateAsync(Supply supply);

    /// <summary>Lightweight single-column update for the inline stock-level dropdown.</summary>
    Task UpdateStatusAsync(int id, SupplyStatus status);

    /// <summary>Hard delete — supplies have no deleted_at column, unlike computer_units/equipment.</summary>
    Task DeleteAsync(int id);
}
