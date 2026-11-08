using System.Security.Claims;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Enums;
using Microsoft.AspNetCore.Components.Authorization;

namespace CLMS.Web.Authentication;

public sealed class CurrentUser(IHttpContextAccessor accessor, AuthenticationStateProvider state,
    IUserRepository users) : ICurrentUser
{
    public async Task<Guid> RequireAdminAsync()
    {
        // HTTP APIs use HttpContext; interactive Blazor events use the circuit's authentication state.
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            principal = (await state.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new UnauthorizedAccessException("Sign in as an administrator to register users.");
        var actor = await users.FindByIdAsync(id);
        // Check current data instead of trusting a stale Admin claim. SuperAdmin will replace this later.
        if (actor is null || actor.DeletedAt.HasValue || actor.UserRole != Role.Admin || !actor.PasswordChanged)
            throw new UnauthorizedAccessException("Only an active administrator can register users.");
        return id;
    }
}
