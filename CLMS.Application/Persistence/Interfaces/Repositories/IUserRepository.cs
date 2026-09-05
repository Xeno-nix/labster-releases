namespace CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Enums;
public interface IUserRepository
{
    Task<IEnumerable<User>> GetByRoleAsync(Role role);
}