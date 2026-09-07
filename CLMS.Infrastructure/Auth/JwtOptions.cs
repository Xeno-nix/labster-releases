namespace CLMS.Infrastructure.Auth;

public sealed class JwtOptions
{
    public string HashAlgorithm { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Issuer) &&
               !string.IsNullOrWhiteSpace(HashAlgorithm) &&
               !string.IsNullOrWhiteSpace(SecretKey) &&
               AccessTokenExpirationMinutes > 0;
    }
}
