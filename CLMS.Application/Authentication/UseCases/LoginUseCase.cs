using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
using CLMS.Shared.AuthRequests;

public sealed class LoginUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public LoginUseCase(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IRefreshTokenHasher refreshTokenHasher,
        IRefreshTokenRepository refreshTokenRepository)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _refreshTokenHasher = refreshTokenHasher;
        _refreshTokenRepository = refreshTokenRepository;
    }

    public async Task<AuthenticationResponse> ExecuteAsync(
        LoginRequest request)
    {
        var user =
            await _userRepository.FindByEmailAsync(request.Email);

        if (user is null)
            throw new UnauthorizedAccessException(
                "Invalid credentials.");

        var passwordValid =
            _passwordHasher.Verify(
                request.Password,
                user.PasswordHash);

        if (!passwordValid)
            throw new UnauthorizedAccessException(
                "Invalid credentials.");

        var accessToken =
            _tokenService.GenerateAccessToken(user);

        var refreshToken =
            _tokenService.GenerateRefreshToken();

        var refreshTokenHash =
            _refreshTokenHasher.Hash(refreshToken);

        var refreshTokenEntity =
            RefreshToken.Create(
                Guid.NewGuid(),
                user.Id,
                refreshTokenHash,
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(7));

        await _refreshTokenRepository.AddAsync(
            refreshTokenEntity);

        return new AuthenticationResponse(
            accessToken,
            refreshToken);
    }
}