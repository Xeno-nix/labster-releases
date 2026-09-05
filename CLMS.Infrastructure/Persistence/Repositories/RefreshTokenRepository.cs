using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;

namespace CLMS.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    public Task AddAsync(RefreshToken refreshToken)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<RefreshToken>> FindAllByUserIdAsync(Guid userId)
    {
        throw new NotImplementedException();
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash)
    {
        throw new NotImplementedException();
    }

    public Task UpdateAsync(RefreshToken refreshToken)
    {
        throw new NotImplementedException();
    }
}
