using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Persistence.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash);
    
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(
        Guid userId);

    Task<bool> AddAsync(RefreshToken refreshToken);

    Task<bool> RevokeAsync(Guid refreshTokenId, DateTime revokedAt);
}

