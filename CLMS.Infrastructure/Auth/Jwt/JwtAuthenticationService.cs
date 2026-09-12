using CLMS.Application.Authentication.Interfaces;
using CLMS.Domain.Entities.Authentication;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using CLMS.Shared.Dtos;
using Microsoft.Extensions.Options;

namespace CLMS.Infrastructure.Auth;

public class JwtAuthenticationService : ITokenService
{
    private readonly JwtOptions _config;
    public JwtAuthenticationService(IOptions<JwtOptions> config)
    {
        _config = config.Value;
    }

    /// <summary>
    /// Creates a JWT Access Token made of 3 parts:
    /// <list type="number">
    ///     <item>Signature for verifying token</item>
    ///     <item>Claims associated with the user/resource owner</item>
    ///     <item>Metadata (i.e. expiry date, issuer)</item>
    /// </list>
    /// </summary>
    /// <param name="user">The resource owner requesting access</param>
    /// <returns>A base64 encoded JSON token</returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public string GenerateAccessToken(UserClaimsDto user)
    {
        
        if (user == null) throw new ArgumentNullException(nameof(user), "user cannot be null.");
        if (!_config.IsValid()) throw new InvalidOperationException("Invalid JWT Configuration");
        
        // signing credentials
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.Key));
        var signature = new SigningCredentials(key, _config.SigningAlgorithm);

        // claims
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,user.UserId.ToString()),
            new(ClaimTypes.Email,user.Email),
            new(ClaimTypes.Role,user.UserRole.ToString())
        };

        // expiry
        var expiry = DateTime.UtcNow.Add(AccessTokenLifetime);

        var token = new JwtSecurityToken(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims:claims, 
            signingCredentials:signature,
            expires:expiry 
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes); 
    }
    
    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_config.AccessTokenExpirationMinutes);
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromHours(_config.RefreshTokenExpirationHours);
}
