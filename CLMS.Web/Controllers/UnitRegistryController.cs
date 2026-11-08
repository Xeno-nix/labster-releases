using CLMS.Application.UnitRegistry;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "UnitRegistryAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UnitRegistryController(UnitRegistryService registry) : ControllerBase
{
    [HttpGet("units")]
    public async Task<IActionResult> Units(int? laboratoryId, string? search, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        Ok(await registry.UnitsAsync(laboratoryId, search, page, pageSize, ct));
    [HttpGet("unit-registrations")]
    public async Task<IActionResult> Registrations(string? status = "Pending", int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        Ok(await registry.RegistrationsAsync(status, page, pageSize, ct));
    [HttpPost("unit-registrations/{id:guid}/approve"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, RegistrationDecision decision, CancellationToken ct)
    {
        await registry.DecideAsync(id, true, decision.PhysicalCodeVerified, ct);
        return NoContent();
    }
    [HttpPost("unit-registrations/{id:guid}/reject"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        await registry.DecideAsync(id, false, false, ct);
        return NoContent();
    }
    [HttpDelete("units/{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await registry.DeleteAsync(id, ct);
        return NoContent();
    }
    [HttpPost("units/{id:guid}/disable"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await registry.DisableAsync(id, ct);
        return NoContent();
    }
}
