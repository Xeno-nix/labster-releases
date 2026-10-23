namespace CLMS.Application.Identity.Exceptions;

/// <summary>
/// Too many failed login attempts. Login is refused until <see cref="LockoutEnd"/> (UTC).
/// </summary>
public sealed class AccountLockedException(DateTime lockoutEnd)
    : UnauthorizedAccessException("Account is locked. Try again later.")
{
    public DateTime LockoutEnd { get; } = lockoutEnd;
}
