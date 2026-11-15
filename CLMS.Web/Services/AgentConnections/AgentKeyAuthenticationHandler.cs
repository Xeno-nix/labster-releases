using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CLMS.Web.Services.AgentConnections;

public sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<AgentConnectionOptions> devices)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AgentKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.IsHttps)
            return Task.FromResult(AuthenticateResult.Fail("Agent connections require HTTPS."));

        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var key = authorization[7..].Trim();
        if (key.Length is < 32 or > 512)
            return Task.FromResult(AuthenticateResult.Fail("Invalid device credential."));

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        AgentDeviceCredential? match = null;
        foreach (var device in devices.CurrentValue.Devices)
        {
            if (device.ComputerUnitId == Guid.Empty || device.KeySha256.Length != 64) continue;
            byte[] expectedHash;
            try { expectedHash = Convert.FromHexString(device.KeySha256); }
            catch (FormatException) { continue; }
            if (!CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash)) continue;
            // A reused key must never authenticate as two different units.
            if (match is not null)
                return Task.FromResult(AuthenticateResult.Fail("Ambiguous device credential."));
            match = device;
        }

        if (match is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid device credential."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, match.ComputerUnitId.ToString())], SchemeName);
        var properties = new AuthenticationProperties
        {
            // Force periodic reauthentication so rotated/removed keys stop reconnecting.
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), properties, SchemeName)));
    }
}
