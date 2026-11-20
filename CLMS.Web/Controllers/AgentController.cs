using System.Globalization;
using CLMS.Application.UnitRegistry;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/agent")]
[IgnoreAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16 * 1024)]
public sealed class AgentController(UnitRegistryService registry, DeviceAuthenticationService authentication,
    ILogger<AgentController> logger) : ControllerBase
{
    /// <summary>Returns registration laboratory choices through the rate-limited discovery endpoint.</summary>
    [AllowAnonymous, HttpGet("laboratories"), EnableRateLimiting("AgentDiscovery")]
    public async Task<IActionResult> Laboratories() => Ok(await registry.LaboratoriesAsync());

    /// <summary>Creates a pending registration without issuing permanent credentials and returns its private receipt.</summary>
    [AllowAnonymous, HttpPost("registrations"), EnableRateLimiting("AgentRegistration")]
    public async Task<IActionResult> Register(RegisterUnitRequest request, CancellationToken ct) =>
        StatusCode(201, await registry.RegisterAsync(request, ct));

    /// <summary>Returns registration state after authenticating the requesting agent with its private registration secret.</summary>
    [AllowAnonymous, HttpPost("registrations/{id:guid}/status"), EnableRateLimiting("AgentPolling")]
    public async Task<IActionResult> Status(Guid id, RegistrationSecret request, CancellationToken ct) =>
        Ok(await registry.StatusAsync(id, request.Secret, ct));

    /// <summary>Creates a completion proof challenge after checking the private registration secret.</summary>
    [AllowAnonymous, HttpPost("registrations/{id:guid}/completion-challenge"), EnableRateLimiting("AgentClaim")]
    public async Task<IActionResult> CompletionChallenge(Guid id, RegistrationSecret request, CancellationToken ct) =>
        Ok(await authentication.CompletionChallengeAsync(id, request.Secret, ct));

    /// <summary>Activates an approved key once after receipt authentication and private-key possession proof.</summary>
    [AllowAnonymous, HttpPost("registrations/{id:guid}/claim"), EnableRateLimiting("AgentClaim")]
    public async Task<IActionResult> Claim(Guid id, ClaimUnitRequest request, CancellationToken ct)
    {
        var identity = await authentication.CompleteAsync(id, request, ct);
        logger.LogInformation("Agent registration completed for unit {UnitId}, credential {CredentialId}", identity.ComputerUnitId, identity.CredentialId);
        return Ok(identity);
    }

    /// <summary>Issues a short-lived challenge for an authorized device identity.</summary>
    [AllowAnonymous, HttpPost("auth/challenge"), EnableRateLimiting("AgentAuthentication")]
    public async Task<IActionResult> Challenge(DeviceChallengeRequest request, CancellationToken ct) =>
        Ok(await authentication.ChallengeAsync(request, ct));

    /// <summary>Consumes one signed challenge and returns an agent-only access token.</summary>
    [AllowAnonymous, HttpPost("auth/token"), EnableRateLimiting("AgentAuthentication")]
    public async Task<IActionResult> Token(DeviceProofRequest request, CancellationToken ct)
    {
        var result = await authentication.TokenAsync(request, ct);
        logger.LogInformation("Agent authenticated for unit {UnitId}, credential {CredentialId}", result.ComputerUnitId, result.CredentialId);
        return Ok(result);
    }

    /// <summary>Binds a replacement public key to a fresh rotation challenge for the current authenticated key.</summary>
    [Authorize(Policy = "AgentOnly"), HttpPost("auth/rotation-challenge"), EnableRateLimiting("AgentAuthentication")]
    public async Task<IActionResult> RotationChallenge(RotationChallengeRequest request, CancellationToken ct) =>
        Ok(await authentication.RotationChallengeAsync(Guid.Parse(User.FindFirst("computer_unit_id")!.Value),
            Guid.Parse(User.FindFirst("device_credential_id")!.Value), request, ct));

    /// <summary>Requires both key proofs; no bearer token is needed to recover from a lost rotation response.</summary>
    [AllowAnonymous, HttpPost("auth/rotate"), EnableRateLimiting("AgentAuthentication")]
    public async Task<IActionResult> Rotate(RotationProofRequest request, CancellationToken ct)
    {
        var identity = await authentication.RotateAsync(request, ct);
        logger.LogInformation("Agent credential rotated for unit {UnitId}, credential {CredentialId}", identity.ComputerUnitId, identity.CredentialId);
        return Ok(identity);
    }

    /// <summary>Records availability for the authenticated unit using validated claims and the server-observed source IP.</summary>
    [Authorize(Policy = "AgentOnly"), HttpPost("heartbeat"), EnableRateLimiting("AgentHeartbeat")]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        await registry.HeartbeatAsync(Guid.Parse(User.FindFirst("computer_unit_id")!.Value),
            int.Parse(User.FindFirst("credential_generation")!.Value, CultureInfo.InvariantCulture),
            Guid.Parse(User.FindFirst("device_credential_id")!.Value), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return NoContent();
    }
}
