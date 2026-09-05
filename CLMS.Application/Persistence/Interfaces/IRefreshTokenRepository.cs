using CLMS.Domain.Entities.Authentication;

namespace CLMS.Application.Persistence.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(
        string tokenHash);

    Task<IReadOnlyList<RefreshToken>> FindAllByUserIdAsync(
        Guid userId);

    Task AddAsync(
        RefreshToken refreshToken);

    Task UpdateAsync(
        RefreshToken refreshToken);
}

