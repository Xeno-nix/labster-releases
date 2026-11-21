using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;

namespace CLMS.Application.Services;

public class ComputerUnitService
{
    private readonly ICurrentUser actor;
    private readonly IComputerUnitRepository _repository;

    public ComputerUnitService(IComputerUnitRepository repository, ICurrentUser actor)
    {
        this.actor = actor;
        _repository = repository;
    }

    public async Task<Guid> CreateAsync(UpsertComputerUnitDTO dto)
    {
        await actor.RequirePrivilegeAsync(Privileges.UnitsManage);
        if (dto.UnitNumber == 0 || dto.LaboratoryId <= 0) throw new ArgumentException("Select a laboratory and unit number 1–255.");
        if (await _repository.ExistsAtLabAndNumberAsync(dto.LaboratoryId, dto.UnitNumber))
        {
            throw new InvalidOperationException(
                $"Unit {dto.UnitNumber} already exists in this laboratory.");
        }

        var computerUnit = new ComputerUnit(
            Guid.NewGuid(),
            dto.UnitName,
            dto.UnitNumber,
            dto.LaboratoryId);

        await _repository.CreateAsync(computerUnit);

        return computerUnit.Id;
    }

    public async Task<IEnumerable<ComputerUnitDTO>> GetAllAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.UnitsView);
        return await _repository.GetAllAsync();
    }

    public async Task<ComputerUnitDTO?> GetByIdAsync(Guid id)
    {
        await actor.RequirePrivilegeAsync(Privileges.UnitsView);
        return await _repository.GetByIdAsync(id);
    }

    public async Task UpdateAsync(Guid id, UpsertComputerUnitDTO dto)
    {
        await actor.RequirePrivilegeAsync(Privileges.UnitsManage);
        if (dto.UnitNumber == 0 || dto.LaboratoryId <= 0) throw new ArgumentException("Select a laboratory and unit number 1–255.");
        if (await _repository.ExistsAtLabAndNumberAsync(dto.LaboratoryId, dto.UnitNumber, excludingId: id))
        {
            throw new InvalidOperationException(
                $"Unit {dto.UnitNumber} already exists in this laboratory.");
        }

        var computerUnit = new ComputerUnit(
            id,
            dto.UnitName,
            dto.UnitNumber,
            dto.LaboratoryId);

        await _repository.UpdateAsync(computerUnit);
    }

    public async Task DeleteAsync(Guid id)
    {
        await actor.RequirePrivilegeAsync(Privileges.UnitsManage);
        await _repository.DeleteAsync(id);
    }
}
