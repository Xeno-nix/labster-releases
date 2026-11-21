using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;

namespace CLMS.Application.Services;

public class SupplyService 
{
    private readonly ICurrentUser actor;
    private readonly ISupplyRepository _repository;

    public SupplyService(ISupplyRepository repository, ICurrentUser actor)
    {
        this.actor = actor;
        _repository = repository;
    }

    public async Task<int> CreateAsync(UpsertSupplyDTO dto)
    {
        await actor.RequirePrivilegeAsync(Privileges.InventoryManage);
        var supply = new Supply(0, dto.Name, dto.Category, dto.Unit, ParseStatus(dto.Status), dto.Remarks);
        return await _repository.CreateAsync(supply);
    }

    public async Task<IEnumerable<SupplyDTO>> GetAllAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.InventoryView);
        return await _repository.GetAllAsync();
    }

    public async Task<SupplyDTO?> GetByIdAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.InventoryView);
        return await _repository.GetByIdAsync(id);
    }

    public async Task UpdateAsync(int id, UpsertSupplyDTO dto)
    {
        await actor.RequirePrivilegeAsync(Privileges.InventoryManage);
        var supply = new Supply(id, dto.Name, dto.Category, dto.Unit, ParseStatus(dto.Status), dto.Remarks);
        await _repository.UpdateAsync(supply);
    }

    public async Task UpdateStatusAsync(int id, string status)
    {
        await actor.RequirePrivilegeAsync(Privileges.InventoryManage);
        await _repository.UpdateStatusAsync(id, ParseStatus(status));
    }

    public async Task DeleteAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.InventoryManage);
        await _repository.DeleteAsync(id);
    }

    private static SupplyStatus ParseStatus(string value)
    {
        var normalized = value.Replace(" ", "").Replace("_", "").Replace("-", "");

        if (Enum.TryParse<SupplyStatus>(normalized, ignoreCase: true, out var status))
            return status;

        throw new ArgumentException($"Unrecognized supply status: '{value}'.");
    }
}
