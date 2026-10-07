using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Persistence.Interfaces;
public interface IExternalIdentityRepository
{
    Task<ExternalIdentity> FindByProviderAndUserIdAsync(string provider, string providerUserId);
    Task<ExternalIdentity> FindByIdAsync(Guid id);
    Task<IReadOnlyList<ExternalIdentity>> FindAllByUserIdAsync(Guid userId);
    Task CreateAsync(ExternalIdentity identity);
    Task DeleteAsync(ExternalIdentity identity);
}
