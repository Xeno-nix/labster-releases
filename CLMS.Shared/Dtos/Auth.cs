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
    Role UserRole
);

public sealed record AuthTokensDto(
    string AccessToken,
    string RefreshToken);