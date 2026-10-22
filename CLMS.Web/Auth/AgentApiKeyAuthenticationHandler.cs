using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CLMS.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CLMS.Web.Auth;

/// <summary>
/// Authenticates lab-PC agents via the X-Agent-Key header. The key is looked up
/// by its SHA-256 digest against computer_units.agent_api_key_hash — the raw key
/// is never stored server-side. A successful lookup yields unit_id + unit_serial
/// claims so endpoints never trust a serial number from the request body.
/// </summary>
public class AgentApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IComputerUnitRepository computerUnitRepository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AgentKey";
    public const string UnitIdClaim = "unit_id";
    public const string UnitSerialClaim = "unit_serial";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Agent-Key", out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues))
        {
            return AuthenticateResult.NoResult();
        }

        var keyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(keyValues.ToString().Trim()))).ToLowerInvariant();

        var identity = await computerUnitRepository.GetIdentityByApiKeyHashAsync(keyHash)
            .ConfigureAwait(false);

        if (identity is null)
            return AuthenticateResult.Fail("Invalid agent API key.");

        var (unitId, serialNumber) = identity.Value;
        var claims = new[]
        {
            new Claim(UnitIdClaim, unitId.ToString()),
            new Claim(UnitSerialClaim, serialNumber),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
