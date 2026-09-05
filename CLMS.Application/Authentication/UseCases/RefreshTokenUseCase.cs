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
        var tokenHash =
            _refreshTokenHasher.Hash(refreshToken);

        var storedToken =
            await _refreshTokenRepository.FindByHashAsync(
                tokenHash);

        if (storedToken is null)
            throw new UnauthorizedAccessException(
                "Invalid refresh token.");

        if (!storedToken.IsActive)
            throw new UnauthorizedAccessException(
                "Refresh token is expired or revoked.");

        var user =
            await _userRepository.FindByIdAsync(
                storedToken.UserId);

        if (user is null)
            throw new UnauthorizedAccessException(
                "User does not exist.");

        // Rotate refresh token
        storedToken.Revoke();

        var newAccessToken =
            _tokenService.GenerateAccessToken(user);

        var newRefreshToken =
            _tokenService.GenerateRefreshToken();

        var newRefreshTokenHash =
            _refreshTokenHasher.Hash(newRefreshToken);

        var newRefreshTokenEntity =
            RefreshToken.Create(
                Guid.NewGuid(),
                user.Id,
                newRefreshTokenHash,
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(7));

        await _refreshTokenRepository.UpdateAsync(
            storedToken);

        await _refreshTokenRepository.AddAsync(
            newRefreshTokenEntity);

        return new AuthenticationResponse(
            newAccessToken,
            newRefreshToken);
    }
}

