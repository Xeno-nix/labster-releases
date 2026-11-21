namespace CLMS.Application.Identity.Interfaces;

/// <summary>Resolves the actor from the host, never from registration input.</summary>
public interface ICurrentUser
{
    Task<Guid> RequireAdminAsync();
    Task<CLMS.Domain.Entities.Identity.User> GetActorAsync() => throw new UnauthorizedAccessException("A current human actor is required.");
    Task<Guid> RequirePrivilegeAsync(string privilege) => throw new UnauthorizedAccessException("The required privilege is missing.");
    Task<Guid> RequireSuperAdminAsync() => throw new UnauthorizedAccessException("SuperAdmin access is required.");
    Task<Guid> RequirePrivilegeForAsync(System.Security.Claims.ClaimsPrincipal principal, string privilege) => throw new UnauthorizedAccessException("The required privilege is missing.");
}
