using CLMS.Domain.Entities.Authentication;
using CLMS.Domain.Enums;

namespace CLMS.Application.Persistence.Interfaces;
public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id);
    Task<User?> FindByEmailAsync(string email);
    Task<IReadOnlyList<User>> FindAllAsync();
    Task<IReadOnlyList<User>> FindAllByRoleAsync(Role role);
    Task<bool> AddAsync(User user);
    Task<bool> UpdateAsync(User user);
    Task<bool> DeleteAsync(User user);
}
