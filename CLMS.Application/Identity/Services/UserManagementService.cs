using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;

namespace CLMS.Application.Identity.Services;

public sealed class UserManagementService(
    IUserRepository userRepository)
{
    /// <summary>
    /// Retrieves all active user accounts
    /// </summary>
    /// <returns></returns>
    public async Task<List<User>> GetUsers()
    {
        return (from user in await userRepository.FindAllAsync()
            where user.DeletedAt is null
            select user).ToList();
    }

    public async Task<User?> GetUserById(Guid id)
    {
        return await userRepository.FindByIdAsync(id);
    }

    public async Task DeleteUser(Guid id)
    {
        var user = await GetUserById(id);
        
        if (user is null || user.DeletedAt is not null)
            return;
        
        await userRepository.DeleteAsync(user);
    }

    public async Task UpdateUser(Guid userId, Role? role, string firstName, string lastName, string? middleName = null,
        string? suffix = null)
    {
        var storedUser = await userRepository.FindByIdAsync(userId);
        if (storedUser is null) return;
        if (storedUser.DeletedAt is not null) return;
        
        if (role.HasValue) storedUser.UserRole = role.Value;
        
        storedUser.FirstName = new Name(firstName);
        storedUser.LastName = new Name(lastName);
        
        if (middleName is not null) storedUser.MiddleName = new Name(middleName);
        if (suffix is not null) storedUser.Suffix = new Name(suffix);
        
        await userRepository.UpdateAsync(storedUser);
    }
}
