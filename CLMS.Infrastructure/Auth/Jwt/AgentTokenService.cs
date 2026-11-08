using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CLMS.Application.UnitRegistry;
using CLMS.Shared.Dtos;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CLMS.Infrastructure.Auth;

public sealed class AgentAuthOptions
{
    public string Audience { get; set; } = "CLMS.Agent";
    public int AccessMinutes { get; set; } = 15;

}

public sealed class AgentTokenService(IOptions<JwtOptions> jwt, IOptions<AgentAuthOptions> agent) : IAgentTokenService
{
    /// <summary>Maps supported HMAC signature identifiers to the JWT algorithm names used consistently for issuance and validation.</summary>
    public static string NormalizeAlgorithm(string algorithm) => algorithm switch
    {
        SecurityAlgorithms.HmacSha256Signature => SecurityAlgorithms.HmacSha256,
        SecurityAlgorithms.HmacSha384Signature => SecurityAlgorithms.HmacSha384,
        SecurityAlgorithms.HmacSha512Signature => SecurityAlgorithms.HmacSha512,
        _ => algorithm
    };
    /// <summary>Checks issuer, audience, and the minimum signing-key byte length for the selected HMAC algorithm.</summary>
    public static bool IsValidSigningConfiguration(JwtOptions options)
    {
        var minimumBytes = NormalizeAlgorithm(options.SigningAlgorithm) switch
        {
            SecurityAlgorithms.HmacSha256 => 32, SecurityAlgorithms.HmacSha384 => 48,
            SecurityAlgorithms.HmacSha512 => 64, _ => int.MaxValue
        };
        return !string.IsNullOrWhiteSpace(options.Issuer) && !string.IsNullOrWhiteSpace(options.Audience) && !string.IsNullOrWhiteSpace(options.Key) &&
            Encoding.UTF8.GetByteCount(options.Key) >= minimumBytes;
    }
    public string Audience => agent.Value.Audience;
    /// <summary>Issues a short-lived agent JWT bound to the unit, credential generation, and persistent credential without adding human role claims.</summary>
    public AgentTokens Issue(Guid unitId, int generation, Guid credentialId, DateTime now)
    {
        var expiry = now.AddMinutes(agent.Value.AccessMinutes);
        var token = new JwtSecurityToken(jwt.Value.Issuer, agent.Value.Audience,
        [
            new(JwtRegisteredClaimNames.Sub, unitId.ToString()), new("principal_type", "agent"),
            new("computer_unit_id", unitId.ToString()), new("credential_generation", generation.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("device_credential_id", credentialId.ToString()), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ], now, expiry, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)), NormalizeAlgorithm(jwt.Value.SigningAlgorithm)));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expiry, unitId, generation, credentialId);
    }
    /// <summary>Generates a cryptographically random 256-bit private registration secret.</summary>
    public string NewSecret() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    /// <summary>Generates an eight-character verification code from an alphabet omitting ambiguous digits and letters.</summary>
    public string NewCode()
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        return new string(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }
}
