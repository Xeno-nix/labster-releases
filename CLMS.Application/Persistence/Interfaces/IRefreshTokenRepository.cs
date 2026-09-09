using CLMS.Domain.Entities.Authentication;

namespace CLMS.Application.Persistence.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash);

    Task<bool> AddAsync(RefreshToken refreshToken);

    Task<bool> UpdateAsync(RefreshToken refreshToken);
}

