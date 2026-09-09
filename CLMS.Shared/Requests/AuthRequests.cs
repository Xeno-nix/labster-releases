using CLMS.Domain.Enums;

namespace CLMS.Shared.AuthRequests;

public sealed record RegisterRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string? Suffix,
    string Email,
    Role Role,
    string Password);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record AuthenticationResponse(
    string AccessToken,
    string RefreshToken);
    
public sealed record LogoutRequest(
    string RefreshToken);
    
public sealed record RefreshRequest(
    string RefreshToken);   