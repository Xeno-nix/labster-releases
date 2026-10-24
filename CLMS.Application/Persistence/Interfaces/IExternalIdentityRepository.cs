using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Persistence.Interfaces;
public interface IExternalIdentityRepository
{
    Task<ExternalIdentity?> FindAsync(string provider, string providerSub, CancellationToken cancellationToken);
    Task<ExternalIdentity?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    // Task<IReadOnlyList<ExternalIdentity>> FindAllByUserIdAsync(Guid userId);
    Task<bool> CreateAsync(ExternalIdentity identity, CancellationToken cancellationToken);
    // Task DeleteAsync(ExternalIdentity identity);
}

