using CLMS.Domain.Enums;

namespace CLMS.Shared.Dtos;

public sealed record RegisterDto(
    string FirstName,
    string LastName,
    string? MiddleName,
    string? Suffix,
    string Email,
    Role UserRole,
    string Password);

public sealed record AuthenticationRequestDto(
    string Email,
    string Password);

public sealed record UserClaimsDto(
    Guid UserId,
    string Email,
    Role UserRole,
    bool PasswordChanged = true
)
{
    public string SecurityStamp { get; init; } = "";
    public bool TwoFactorVerified { get; init; }
    public bool TwoFactorEnabled { get; init; } = true;
}

/// <summary>The account is saved even when delivery fails. Never return its password in an API response.</summary>
public sealed record RegistrationResultDto(Guid UserId, bool EmailSent);

public sealed record ChangePasswordDto(string CurrentPassword, string NewPassword, string ConfirmPassword);

public sealed record AuthTokensDto(
    string AccessToken,
    string RefreshToken);
