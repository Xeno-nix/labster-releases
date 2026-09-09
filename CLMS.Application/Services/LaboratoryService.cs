using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;


namespace CLMS.Application.Services;

public class LaboratoryService : ILaboratoryService
{
    private readonly ILaboratoryRepository _repository;

    public LaboratoryService(
        ILaboratoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<LaboratoryDTO>> GetAllAsync()
    {
        var laboratories = await _repository.GetAllAsync();

        return laboratories.Select(MapToDto);
    }

    public async Task<LaboratoryDTO?> GetByIdAsync(int id)
    {
        var laboratory = await _repository.GetByIdAsync(id);

        if (laboratory is null)
            return null;

        return MapToDto(laboratory);
    }


    private static LaboratoryDTO MapToDto(
        Laboratory laboratory)
    {
        return new LaboratoryDTO
        {
          Id = laboratory.Id,
          Name = laboratory.Name,
        };
    }
}
