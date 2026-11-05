using CLMS.Domain.Entities.Identity;
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

    /// <summary>Atomically adds one failed login attempt and returns the new count.</summary>
    Task<int> IncrementFailedLoginAttemptsAsync(Guid userId);

    /// <summary>Locks the account until <paramref name="lockoutEnd"/> (UTC) and resets the failed attempt count.</summary>
    Task<bool> LockOutAsync(Guid userId, DateTime lockoutEnd);

    /// <summary>Clears the failed attempt count and any lockout.</summary>
    Task<bool> ResetFailedLoginAttemptsAsync(Guid userId);
}
