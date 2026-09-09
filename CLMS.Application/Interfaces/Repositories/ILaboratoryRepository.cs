
using CLMS.Domain.Entities.Booking;
namespace CLMS.Application.Interfaces.Repositories;

public interface ILaboratoryRepository
{
    Task<IEnumerable<Laboratory>> GetAllAsync();

    Task<Laboratory?> GetByIdAsync(int id);

}