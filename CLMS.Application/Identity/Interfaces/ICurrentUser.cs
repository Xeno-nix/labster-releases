namespace CLMS.Application.Identity.Interfaces;

/// <summary>Resolves the actor from the host, never from registration input.</summary>
public interface ICurrentUser
{
    Task<Guid> RequireAdminAsync();
}
