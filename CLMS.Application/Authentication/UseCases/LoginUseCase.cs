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
    
    // Max number of refresh tokens per user
    private const int MaxSessions = 5;

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

    /// <summary>
    /// Verifies user credentials and issuing access and refresh tokens.
    /// </summary>
    /// <param name="request">request details (email and password)</param>
    /// <returns>Access and Refresh Token</returns>
    /// <exception cref="UnauthorizedAccessException">Returns when credentials are invalid</exception>
    public async Task<AuthenticationResponse> ExecuteAsync(LoginRequest request)
    {
        // find user by email
        var user = await _userRepository.FindByEmailAsync(request.Email);

        // if user not found using email
        if (user is null) throw new UnauthorizedAccessException("Invalid credentials.");

        // checks if the password is correct for the user
        var passwordValid =
            _passwordHasher.Verify(
                request.Password,
                user.PasswordHash);
        
        // invalid password
        if (!passwordValid) throw new UnauthorizedAccessException("Invalid credentials.");
        
        // generate access token
        var accessToken = _tokenService.GenerateAccessToken(user);

        // retrieve active refresh tokens and revoke if number is above MaxSessions (5)
        var activeTokens =
            await _refreshTokenRepository.GetActiveByUserIdAsync(user.Id);

        if (activeTokens.Count >= MaxSessions)
        {
            var oldestToken = activeTokens[0];

            oldestToken.Revoke();

            await _refreshTokenRepository.RevokeAsync(
                oldestToken.Id,
                oldestToken.RevokedAt!.Value);
        }
        
        // generate refresh token 
        var refreshToken = _tokenService.GenerateRefreshToken();
        
        // hashing refresh token for storing
        var refreshTokenHash = _refreshTokenHasher.Hash(refreshToken);
        
        // constructing refresh token entity to be stored in the database.
        // Includes refresh token hash and metadata.
        //
        // Refresh token lifetime is 1 day
        //
        // TODO: Retrieve lifetime from config
        var refreshTokenEntity = RefreshToken.Create(
                user.Id,
                refreshTokenHash,
                DateTime.UtcNow.AddDays(1)
        );

        // store refresh token
        await _refreshTokenRepository.AddAsync(
            refreshTokenEntity);

        // return response
        return new AuthenticationResponse(
            accessToken,
            refreshToken);
    }
}