using CLMS.Application.DTOs.Inventory;

namespace CLMS.Application.Interfaces;

public interface ISupplyService
{
    Task<int> CreateAsync(UpsertSupplyDTO dto);

    Task<IEnumerable<SupplyDTO>> GetAllAsync();

    Task<SupplyDTO?> GetByIdAsync(int id);

    Task UpdateAsync(int id, UpsertSupplyDTO dto);

    /// <summary>Lightweight single-column update for the inline stock-level dropdown.</summary>
    Task UpdateStatusAsync(int id, string status);

    Task DeleteAsync(int id);
}
