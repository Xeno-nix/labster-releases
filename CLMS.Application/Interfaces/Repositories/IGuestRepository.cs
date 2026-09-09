using CLMS.Domain.Entities.Users;

namespace CLMS.Application.Interfaces.Repositories;

public interface IGuestRepository
{
    Task<int> CreateAsync(Guest guest);
    Task<Guest?> GetByIdAsync(int id);
}
