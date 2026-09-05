using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
tusing CLMS.Domain.Enums;
using System.Data;

namespace CLMS.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private IDbConnection _connection;
    
    public UserRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public Task AddAsync(User user)
    {
        throw new NotImplementedException();
    }

    public Task DeleteAsync(User user)
    {
        throw new NotImplementedException();
    }

    public Task<List<User>> FindAllAsync()
    {
        throw new NotImplementedException();
    }

    public Task<List<User>> FindAllByRoleAsync(Role role)
    {
        throw new NotImplementedException();
    }

    public Task<User> FindByEmailAsync(string email)
    {
        throw new NotImplementedException();
    }

    public Task<User> FindByIdAsync(Guid id)
    {
        throw new NotImplementedException();
    }

    public Task UpdateAsync(User user)
    {
        throw new NotImplementedException();
    }
}