using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;

namespace CLMS.Application.Authentication.UseCases;

public class LogoutUseCase
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenHasher _refreshTokenHasher;

    private LogoutUseCase(){}
    
    public LogoutUseCase(
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenHasher refreshTokenHasher)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenHasher = refreshTokenHasher;
    }
    
    public async Task ExecuteAsync(string refreshToken)
    {
        // hash refresh token for verification
        var tokenHash = _refreshTokenHasher.Hash(refreshToken);
        
        // search for token
        var storedToken = await _refreshTokenRepository.FindByHashAsync(tokenHash);

        // end if token is null
        if (storedToken is null)
            return;

        // if refresh token is not revoked 
        if (!storedToken.IsRevoked)
        {
            storedToken.Revoke();

            await _refreshTokenRepository.UpdateAsync(
                storedToken);
        }
    }
}
