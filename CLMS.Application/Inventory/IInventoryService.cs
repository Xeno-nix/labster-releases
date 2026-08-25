namespace CLMS.Application.Inventory;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryEquipmentDto>> GetEquipmentAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventorySupplyDto>> GetSuppliesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryLabDto>> GetLabsAsync(CancellationToken cancellationToken = default);

    Task<InventoryEquipmentDto?> GetEquipmentByIdAsync(int equipmentId, CancellationToken cancellationToken = default);
    Task CreateOrUpdateEquipmentAsync(InventoryEquipmentDto dto, CancellationToken cancellationToken = default);
    Task DeleteEquipmentAsync(int equipmentId, CancellationToken cancellationToken = default);

    Task<InventorySupplyDto?> GetSupplyByIdAsync(int supplyId, CancellationToken cancellationToken = default);
    Task CreateOrUpdateSupplyAsync(InventorySupplyDto dto, CancellationToken cancellationToken = default);
    Task DeleteSupplyAsync(int supplyId, CancellationToken cancellationToken = default);
    Task UpdateSupplyStatusAsync(int supplyId, string status, CancellationToken cancellationToken = default);
}
