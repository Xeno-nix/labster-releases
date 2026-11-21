using System.Security.Claims;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Enums;
using CLMS.Application.Authorization;
using CLMS.Domain.Entities.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace CLMS.Web.Authentication;

public sealed class CurrentUser(
    IHttpContextAccessor accessor,
    AuthenticationStateProvider state,
    IUserRepository users,
    IAccountSecurityCrypto crypto, IPrivilegeResolver privileges) : ICurrentUser
{
    public async Task<User> ValidateAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirstValue("principal_type") != "human"
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new UnauthorizedAccessException("Sign in with a completed human account.");
        var actor = await users.FindByIdAsync(id);
        if (actor is null || actor.DeletedAt.HasValue || actor.IsLockedOut || !actor.PasswordChanged || string.IsNullOrWhiteSpace(actor.PasswordHash) || !Privileges.IsStaff(actor.UserRole)
            || !crypto.StampMatches(actor, principal.FindFirstValue("security_stamp") ?? ""))
            throw new UnauthorizedAccessException("The account changed or is unavailable. Sign in again.");
        return actor;
    }
    public async Task<User> GetActorAsync()
    {
        // HTTP APIs use HttpContext; interactive Blazor events use the circuit's authentication state.
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            principal = (await state.GetAuthenticationStateAsync()).User;
        return await ValidateAsync(principal);
    }
    public async Task<Guid> RequirePrivilegeAsync(string privilege)
    {
        var actor = await GetActorAsync();
        if (!await privileges.HasPrivilegeAsync(actor.Id, privilege))
        {
            if (Privileges.IsProtected(privilege) && privileges is IAuthorizationRepository audit) await audit.RecordDeniedAsync(actor.Id, privilege, "Operation");
            throw new UnauthorizedAccessException("The required privilege is missing.");
        }
        return actor.Id;
    }
    public Task<Guid> RequireSuperAdminAsync() => RequirePrivilegeAsync(Privileges.SecurityManage);
    public async Task<Guid> RequirePrivilegeForAsync(ClaimsPrincipal principal, string privilege)
    {
        var actor = await ValidateAsync(principal);
        if (!await privileges.HasPrivilegeAsync(actor.Id, privilege)) throw new UnauthorizedAccessException("The required privilege is missing.");
        return actor.Id;
    }
    // Compatibility for explicitly administrative callers; operational services use their own privilege.
    public async Task<Guid> RequireAdminAsync()
    {
        var actor = await GetActorAsync();
        if (!Privileges.HasAdministrativeScope(actor.UserRole)) throw new UnauthorizedAccessException("Administrator access is required.");
        return actor.Id;
    }
}
