using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;


namespace CLMS.Application.Services;

public class LaboratoryService 
{
    private readonly ICurrentUser actor;
    private readonly ILaboratoryRepository _repository;

    public LaboratoryService(
        ILaboratoryRepository repository, ICurrentUser actor)
    {
        this.actor = actor;
        _repository = repository;
    }

    public async Task<IEnumerable<LaboratoryDTO>> GetAllAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.LaboratoriesView);
        var laboratories = await _repository.GetAllAsync();

        return laboratories.Select(MapToDto);
    }

    public async Task<LaboratoryDTO?> GetByIdAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.LaboratoriesView);
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
