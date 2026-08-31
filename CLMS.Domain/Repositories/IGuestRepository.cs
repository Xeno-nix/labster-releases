using CLMS.Domain.Entities.Users;

namespace CLMS.Domain.Repositories;

public interface IGuestRepository
{
    Task<Guest> AddAsync(Guest guest, CancellationToken cancellationToken = default);
    Task<Guest?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
