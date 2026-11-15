using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Persistence.Interfaces;
public interface IExternalIdentityRepository
{
    Task<ExternalIdentity?> FindAsync(string provider, string providerSub, CancellationToken cancellationToken);
    Task<ExternalIdentity?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    // Task<IReadOnlyList<ExternalIdentity>> FindAllByUserIdAsync(Guid userId);
    Task<bool> CreateAsync(ExternalIdentity identity, CancellationToken cancellationToken);
    /// <summary>Links an active account under a row lock. Existing links are never replaced.</summary>
    Task LinkGoogleAsync(Guid userId, string email, string subject, string expectedPasswordHash,
        CancellationToken cancellationToken = default);
    // Task DeleteAsync(ExternalIdentity identity);
}

