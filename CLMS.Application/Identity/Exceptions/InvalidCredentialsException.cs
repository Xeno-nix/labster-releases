namespace CLMS.Application.Identity.Exceptions;

/// <summary>
/// Wrong email or password.
/// </summary>
/// <param name="attemptsRemaining">
/// Failed attempts left before lockout. Only set once the account is close to being locked.
/// </param>
public sealed class InvalidCredentialsException(int? attemptsRemaining = null)
    : UnauthorizedAccessException(BuildMessage(attemptsRemaining))
{
    public int? AttemptsRemaining { get; } = attemptsRemaining;

    private static string BuildMessage(int? attemptsRemaining) => attemptsRemaining switch
    {
        null => "Invalid credentials.",
        1 => "Invalid credentials. 1 attempt left before the account is locked.",
        _ => $"Invalid credentials. {attemptsRemaining} attempts left before the account is locked."
    };
}
