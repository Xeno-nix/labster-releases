using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CLMS.Application.Identity.Interfaces;
using CLMS.Domain.Entities.Identity;
using Microsoft.Extensions.Options;

namespace CLMS.Infrastructure.Auth;

public sealed class AccountSecurityCrypto(IOptions<JwtOptions> options) : IAccountSecurityCrypto
{
    // Domain separation keeps this fingerprint distinct from the existing JWT signing operation.
    private readonly byte[] stampKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key),
        Encoding.UTF8.GetBytes("CLMS.HumanSessionStamp.v1"));
    public string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public string GenerateCode() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    public string Hash(string resetToken) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(resetToken)));
    public bool Verify(string resetToken, string hashToken) => Equal(Hash(resetToken), hashToken);
    public string Stamp(User user) => Convert.ToHexStringLower(HMACSHA256.HashData(stampKey, Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { user.Id.ToString("D"), user.PasswordHash, user.Email.Trim().ToLowerInvariant(),
            user.UserRole.ToString(), user.PasswordChanged ? "changed" : "initial", user.TwoFactorEnabled ? "2fa" : "password" }))));
    public bool StampMatches(User user, string stamp) => Equal(Stamp(user), stamp);
    private static bool Equal(string actual, string? expected) => expected is { Length: 64 } &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected));
}
