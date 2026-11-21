using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;

namespace CLMS.Application.Identity.Services;

public sealed class UserManagementService(
    IUserRepository userRepository, IAuthorizationRepository authorization, ICurrentUser actor, IAuthorizationChanges changes)
{
    /// <summary>
    /// Retrieves all active user accounts
    /// </summary>
    /// <returns></returns>
    public async Task<List<User>> GetUsers()
    {
        await actor.RequirePrivilegeAsync(Privileges.UsersView);
        return (from user in await userRepository.FindAllAsync()
            where user.DeletedAt is null
            select user).ToList();
    }

    public async Task<User?> GetUserById(Guid id)
    {
        await actor.RequirePrivilegeAsync(Privileges.UsersView);
        return await userRepository.FindByIdAsync(id);
    }

    public async Task DeleteUser(Guid id)
    {
        var actorId = await actor.RequirePrivilegeAsync(Privileges.UsersDelete);
        await authorization.DeleteManagedUserAsync(actorId, id);
        changes.NotifyChanged();
    }

    public async Task UpdateUser(Guid userId, Role? role, string firstName, string lastName, string? middleName = null,
        string? suffix = null)
    {
        var actorId = await actor.RequirePrivilegeAsync(Privileges.UsersEdit);
        var storedUser = await userRepository.FindByIdAsync(userId);
        if (storedUser is null) return;
        if (storedUser.DeletedAt is not null) return;
        
        if (role.HasValue) storedUser.UserRole = role.Value;
        
        storedUser.FirstName = new Name(firstName);
        storedUser.LastName = new Name(lastName);
        
        if (middleName is not null) storedUser.MiddleName = new Name(middleName);
        if (suffix is not null) storedUser.Suffix = new Name(suffix);
        
        await authorization.SaveManagedUserAsync(actorId, storedUser, false);
        changes.NotifyChanged();
    }
}
