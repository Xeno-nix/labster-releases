using CLMS.Domain.Entities.Authentication;
using CLMS.Domain.Enums;

namespace CLMS.Application.Persistence.Interfaces;
public interface IUserRepository
{
    Task<User> FindByIdAsync(Guid id);
    Task<User> FindByEmailAsync(string email);
    Task<List<User>> FindAllAsync();
    Task<List<User>> FindAllByRoleAsync(Role role);
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(User user);
}
