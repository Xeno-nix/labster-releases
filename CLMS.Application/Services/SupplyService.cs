using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;

namespace CLMS.Application.Services;

public class SupplyService : ISupplyService
{
    private readonly ISupplyRepository _repository;

    public SupplyService(ISupplyRepository repository)
    {
        _repository = repository;
    }

    public Task<int> CreateAsync(UpsertSupplyDTO dto)
    {
        var supply = new Supply(0, dto.Name, dto.Category, dto.Unit, ParseStatus(dto.Status), dto.Remarks);
        return _repository.CreateAsync(supply);
    }

    public Task<IEnumerable<SupplyDTO>> GetAllAsync()
        => _repository.GetAllAsync();

    public Task<SupplyDTO?> GetByIdAsync(int id)
        => _repository.GetByIdAsync(id);

    public Task UpdateAsync(int id, UpsertSupplyDTO dto)
    {
        var supply = new Supply(id, dto.Name, dto.Category, dto.Unit, ParseStatus(dto.Status), dto.Remarks);
        return _repository.UpdateAsync(supply);
    }

    public Task UpdateStatusAsync(int id, string status)
        => _repository.UpdateStatusAsync(id, ParseStatus(status));

    public Task DeleteAsync(int id)
        => _repository.DeleteAsync(id);

    private static SupplyStatus ParseStatus(string value)
    {
        var normalized = value.Replace(" ", "").Replace("_", "").Replace("-", "");

        if (Enum.TryParse<SupplyStatus>(normalized, ignoreCase: true, out var status))
            return status;

        throw new ArgumentException($"Unrecognized supply status: '{value}'.");
    }
}
