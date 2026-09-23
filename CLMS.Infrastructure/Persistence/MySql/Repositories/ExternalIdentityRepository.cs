using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class ExternalIdentityRepository : IExternalIdentityRepository
{
    public Task CreateAsync(ExternalIdentity identity)
    {
        throw new NotImplementedException();
    }

    public Task DeleteAsync(ExternalIdentity identity)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ExternalIdentity>> FindAllByUserIdAsync(Guid userId)
    {
        throw new NotImplementedException();
    }

    public Task<ExternalIdentity> FindByIdAsync(Guid id)
    {
        throw new NotImplementedException();
    }

    public Task<ExternalIdentity> FindByProviderAndUserIdAsync(string provider, string providerUserId)
    {
        throw new NotImplementedException();
    }
}
