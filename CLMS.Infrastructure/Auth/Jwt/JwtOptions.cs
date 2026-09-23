namespace CLMS.Infrastructure.Auth;

public sealed class JwtOptions
{
    public string SigningAlgorithm { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; }
    public int RefreshTokenExpirationHours { get; set; }

    public bool IsValid()
    {
        Console.WriteLine(
        $"""
         SigningAlg : {SigningAlgorithm}
         Issuer : {Issuer}
         Audience : {Audience}
         Key : {Key}
         RefreshTokenExpirationHours : {RefreshTokenExpirationHours}
         AccessTokenExpirationMinutes : {AccessTokenExpirationMinutes}
         """
            );
        
        return !string.IsNullOrWhiteSpace(Issuer) &&
               !string.IsNullOrWhiteSpace(SigningAlgorithm) &&
               !string.IsNullOrWhiteSpace(Key) &&
               AccessTokenExpirationMinutes > 0 &&
               RefreshTokenExpirationHours > 0;
    }
}
