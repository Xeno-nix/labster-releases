using System.Security.Cryptography;

namespace CLMS.Infrastructure.Auth;

public sealed class JwtOptions
{
    public string HashAlgorithm { get; set; } 
    public string Issuer { get; set; }
    public string SecretKey { get; set; }
    public int AccessTokenExpirationMinutes { get; set; }

    private JwtOptions() { }

    public JwtOptions(string issuer,
                      string audience, 
                      string secretKey,
                      int accessTokenExpirationMinutes,
                      string hashAlgorithm)
    {
        Issuer = issuer;
        SecretKey = secretKey;
        AccessTokenExpirationMinutes = accessTokenExpirationMinutes;
        HashAlgorithm = hashAlgorithm;
    }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Issuer) &&
               !string.IsNullOrWhiteSpace(HashAlgorithm) &&
               !string.IsNullOrWhiteSpace(SecretKey) &&
               AccessTokenExpirationMinutes > 0;
    }
}
