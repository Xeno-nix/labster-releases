using CLMS.Application.Authentication.Interfaces;
using CLMS.Domain.Entities.Authentication;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace CLMS.Infrastructure.Auth;

public class JwtAuthenticationService : ITokenService
{
    private readonly JwtOptions _config;
    public JwtAuthenticationService(JwtOptions config)
    {
        _config = config;
    }

    public string GenerateAccessToken(User user)
    {


        if (user == null) throw new ArgumentNullException("User object is null", nameof(user);
        if (!_config.IsValid()) throw new InvalidOperationException("Invalid JWT Configuration");
        

        // signing credentials
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey));

        var signature = new SigningCredentials(key, _config.HashAlgorithm);

        // claims
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,user.Id.ToString()),
            new(ClaimTypes.Email,user.Email),
            new(ClaimTypes.Role,user.UserRole.ToString())
        };

        // expiry
        var expiry = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes);

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: _config.Issuer,
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
}
