using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
using CLMS.Shared.AuthRequests;

namespace CLMS.Application.Authentication.UseCases;

public sealed class RefreshTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;

    public RefreshTokenUseCase(
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenHasher refreshTokenHasher,
        IUserRepository userRepository,
        ITokenService tokenService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenHasher = refreshTokenHasher;
        _userRepository = userRepository;
        _tokenService = tokenService;
    }

    public async Task<AuthenticationResponse> ExecuteAsync(
        string refreshToken)
    {
        // hash refresh token and search if exists or if revoked
        var tokenHash = _refreshTokenHasher.Hash(refreshToken);
        var storedToken = await _refreshTokenRepository.FindByHashAsync(tokenHash);
        
        // not logged in
        if (storedToken is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");
        
        // expired or logged out
        if (!storedToken.IsActive)
            throw new UnauthorizedAccessException("Refresh token is expired or revoked.");
        
        // find user
        var user = await _userRepository.FindByIdAsync(storedToken.UserId);
        
        // user does not exist
        // 
        // NOTE : if mahitabo ni, something fcked up or giallow ang delete pag
        //        delete ug user which is NO! NO! NO!
        if (user is null)
            throw new UnauthorizedAccessException("User does not exist.");

        // Rotate refresh token (revoke and generate new)
        // Revoke refresh token
        storedToken.Revoke();
        
        // Issue new access token 
        var newAccessToken =
            _tokenService.GenerateAccessToken(user);
        
        // Generate new refresh token
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        // Hash for storage
        var newRefreshTokenHash = _refreshTokenHasher.Hash(newRefreshToken);

        // constructing refresh token entity to be stored in the database.
        // Includes refresh token hash and metadata.
        //
        // Refresh token lifetime is 1 day
        var newRefreshTokenEntity =
            RefreshToken.Create(
                user.Id,
                newRefreshTokenHash,
                DateTime.UtcNow.AddHours(_tokenService.RefreshTokenLifetime.Hours));
        
        // update stored token and store newly generated one
        await _refreshTokenRepository.UpdateAsync(storedToken);
        await _refreshTokenRepository.AddAsync(newRefreshTokenEntity);
        
        return new AuthenticationResponse(newAccessToken,newRefreshToken);
    }
}

