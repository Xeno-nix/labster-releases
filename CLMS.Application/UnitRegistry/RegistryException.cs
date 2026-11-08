namespace CLMS.Application.UnitRegistry;

public sealed class RegistryException(string code, string message, int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public static RegistryException Unavailable() => new("registration_unavailable", "Registration is unavailable.", 404);
    public static RegistryException InvalidCredential() => new("invalid_credential", "Device credentials are invalid or revoked.", 401);
}
