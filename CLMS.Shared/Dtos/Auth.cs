using CLMS.Domain.Enums;

namespace CLMS.Shared.Dtos;

public sealed record RegisterRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string? Suffix,
    string Email,
    Role UserRole,
    string Password);

public sealed record AuthenticationRequest(
    string Email,
    string Password);

public sealed record AuthClaims(
    Guid UserId,
    string Email,
    Role UserRole
);

public sealed record CookieLoginResult(
);

public sealed record AuthTokensDto(
    string AccessToken,
    string RefreshToken);
    
public sealed record LogoutRequest(
    string RefreshToken);
    
public sealed record RefreshRequest(
    string RefreshToken);   