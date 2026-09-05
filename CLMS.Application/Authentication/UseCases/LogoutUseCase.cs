using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;

namespace CLMS.Application.Authentication.UseCases;

public class LogoutUseCase
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenHasher _refreshTokenHasher;

    public LogoutUseCase(
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenHasher refreshTokenHasher)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenHasher = refreshTokenHasher;
    }


    public async Task ExecuteAsync(string refreshToken)
    {
        var tokenHash =
            _refreshTokenHasher.Hash(refreshToken);

        var storedToken =
            await _refreshTokenRepository.FindByHashAsync(
                tokenHash);

        if (storedToken is null)
            return;

        if (!storedToken.IsRevoked)
        {
            storedToken.Revoke();

            await _refreshTokenRepository.UpdateAsync(
                storedToken);
        }
    }
}
