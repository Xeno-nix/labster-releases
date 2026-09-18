using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;

namespace CLMS.Application.Services;

public class ComputerUnitService : IComputerUnitService
{
    private readonly IComputerUnitRepository _repository;

    public ComputerUnitService(IComputerUnitRepository repository)
    {
        _repository = repository;
    }

    public async Task<Guid> CreateAsync(UpsertComputerUnitDTO dto)
    {
        if (await _repository.ExistsActiveAtLabAndNumberAsync(dto.LaboratoryId, dto.UnitNumber))
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

    public Task<IEnumerable<ComputerUnitDTO>> GetAllAsync()
        => _repository.GetAllAsync();

    public Task<ComputerUnitDTO?> GetByIdAsync(Guid id)
        => _repository.GetByIdAsync(id);

    public async Task UpdateAsync(Guid id, UpsertComputerUnitDTO dto)
    {
        if (await _repository.ExistsActiveAtLabAndNumberAsync(dto.LaboratoryId, dto.UnitNumber, excludingId: id))
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

    public Task DeleteAsync(Guid id)
        => _repository.DeleteAsync(id);
}
