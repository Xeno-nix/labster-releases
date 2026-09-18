using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface ILaboratoryService
{
    Task<IEnumerable<LaboratoryDTO>> GetAllAsync();

    Task<LaboratoryDTO?> GetByIdAsync(int id);

}
