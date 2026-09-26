using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.ValueObjects;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Services;

public sealed class AuthenticationService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenHasher refreshTokenHasher,
    IRefreshTokenRepository refreshTokenRepository)
{
    private const int MaxSessions = 5;
    
    /// <summary>
    /// Registers User
    /// </summary>
    /// <param name="request"> Request details. <see cref="RegisterRequest"/></param>
    /// <exception cref="InvalidOperationException">Email already been used.</exception>
    public async Task RegisterAsync(RegisterDto request)
    {
        // Search if user with same email already exists
        var existingUser = await userRepository.FindByEmailAsync(request.Email);

        if (existingUser is not null)
            throw new InvalidOperationException("A user with this email already exists.");

        // hash password
        var passwordHash = passwordHasher.Hash(request.Password);

        Console.WriteLine("RegisterUseCase : " + request.UserRole);
        
        // generate new user entity
        var user = new User(
            Guid.NewGuid(),
            new Name(request.FirstName),
            new Name(request.LastName),
            request.MiddleName is not null
                ? new Name(request.MiddleName)
                : null,
            request.Suffix is not null
                ? new Name(request.Suffix)
                : null,
            request.Email,
            request.UserRole,
            passwordHash);

        // Saves user entity
        await userRepository.AddAsync(user);    
    }

    public async Task<AuthTokensDto> JwtLogin(AuthenticationRequestDto requestDto)
    {
        var userClaims = await AuthenticateUserAsync(requestDto);
        
        // generate access token
        var accessToken = tokenService.GenerateAccessToken(userClaims);

        // retrieve active refresh tokens and revoke if number is above MaxSessions (5)
        var activeTokens =
            await refreshTokenRepository.GetActiveByUserIdAsync(userClaims.UserId);

        if (activeTokens.Count >= MaxSessions)
        {
            var oldestToken = activeTokens[0];

            oldestToken.Revoke();

            await refreshTokenRepository.RevokeAsync(
                oldestToken.Id,
                oldestToken.RevokedAt!.Value);
        }
        
        // generate refresh token 
        var refreshToken = tokenService.GenerateRefreshToken();
        
        // hashing refresh token for storing
        var refreshTokenHash = refreshTokenHasher.Hash(refreshToken);
        
        // constructing refresh token entity to be stored in the database.
        // Includes refresh token hash and metadata.
        var refreshTokenEntity = RefreshToken.Create(
            userClaims.UserId,
            refreshTokenHash,
            DateTime.UtcNow.Add(tokenService.RefreshTokenLifetime)
        );

        // store refresh token
        await refreshTokenRepository.AddAsync(
            refreshTokenEntity);

        // return response
        return new AuthTokensDto(
            accessToken,
            refreshToken);
    }

    public async Task Logout(string refreshToken)
    {
        // hash refresh token for verification
        var tokenHash = refreshTokenHasher.Hash(refreshToken);
        
        // search for token
        var storedToken = await refreshTokenRepository.FindByHashAsync(tokenHash);

        // end if token is null
        if (storedToken is null)
            return;

        // if refresh token is not revoked 
        if (!storedToken.IsRevoked)
        {
            storedToken.Revoke();

            await refreshTokenRepository.RevokeAsync(storedToken.Id,DateTime.UtcNow);
        }
    }
    
    public async Task<AuthTokensDto> Refresh(string refreshToken)
    {
        // hash refresh token and search if exists or if revoked
        var tokenHash = refreshTokenHasher.Hash(refreshToken);
        var storedToken = await refreshTokenRepository.FindByHashAsync(tokenHash);
        
        // not logged in
        if (storedToken is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");
        
        // expired or logged out
        if (!storedToken.IsActive)
            throw new UnauthorizedAccessException("Refresh token is expired or revoked.");
        
        // find user
        var user = await userRepository.FindByIdAsync(storedToken.UserId);
        
        // user does not exist
        // 
        // NOTE : if mahitabo ni, something namali up or giallow ang delete pag
        //        delete ug user which is NO! NO! NO!
        if (user is null)
            throw new UnauthorizedAccessException("User does not exist.");
        
        // Issue new access token 
        var newAccessToken =
            tokenService.GenerateAccessToken(new (user.Id,user.Email,user.UserRole));
        
        // Generate new refresh token
        var newRefreshToken = tokenService.GenerateRefreshToken();

        // Hash for storage
        var newRefreshTokenHash = refreshTokenHasher.Hash(newRefreshToken);

        // constructing refresh token entity to be stored in the database.
        // Includes refresh token hash and metadata.
        //
        // Refresh token lifetime is 1 day
        var newRefreshTokenEntity =
            RefreshToken.Create(
                user.Id,
                newRefreshTokenHash,
                DateTime.UtcNow.AddHours(tokenService.RefreshTokenLifetime.Hours));
        
        // revoked old token and store newly generated one
        await refreshTokenRepository.RevokeAsync(storedToken.Id, DateTime.UtcNow);
        await refreshTokenRepository.AddAsync(newRefreshTokenEntity);
        
        return new AuthTokensDto(newAccessToken,newRefreshToken);
    }

    /// <summary>
    /// Verifies credential of user and returns user info
    /// </summary>
    /// <param name="requestDto"></param>
    /// <returns></returns>
    /// <exception cref="UnauthorizedAccessException"></exception>
    public async Task<UserClaimsDto> AuthenticateUserAsync(AuthenticationRequestDto requestDto)
    {
        // find user by email
        var user = await userRepository.FindByEmailAsync(requestDto.Email);

        // if user not found using email
        if (user is null) throw new UnauthorizedAccessException("Invalid credentials.");

        // checks if the password is correct for the user
        var passwordValid =
            passwordHasher.Verify(
                requestDto.Password,
                user.PasswordHash);
        
        // invalid password
        if (!passwordValid) throw new UnauthorizedAccessException("Invalid credentials.");
        
        return new UserClaimsDto(
            user.Id,
            user.Email,
            user.UserRole);
    } 
}