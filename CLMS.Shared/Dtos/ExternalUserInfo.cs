using CLMS.Domain.Enums;

namespace CLMS.Shared.Contracts;

/// <summary>
/// 
/// </summary>
/// <param name="UserId"></param>
/// <param name="Email"></param>
/// <param name="UserRole"></param>
public sealed record ExternalLoginUserDto(
    Guid UserId,
    string Email,
    Role UserRole,
    bool PasswordChanged = true,
    bool RequiresLink = false);

/// <summary>
/// 
/// </summary>
/// <param name="Provider"></param>
/// <param name="ProviderSubject"></param>
/// <param name="Email"></param>
public sealed record ExternalLoginRequest(
    string Provider,
    string ProviderSubject,
    string Email,
    bool EmailVerified = false,
    string? HostedDomain = null);
