using System.Security.Claims;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Enums;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Authorize(Policy = "JwtPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AgentSettingsController(IUserRepository users) : ControllerBase
{
    // Read the current account role, rather than trusting a role supplied by the desktop or an old JWT.
    [HttpGet("api/agent/settings/access")]
    public async Task<IActionResult> Access()
    {
        if (User.Identity?.IsAuthenticated != true || User.FindFirstValue("principal_type") != "human" ||
            !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var user = await users.FindByIdAsync(id);
        if (user is null || user.DeletedAt.HasValue || user.IsLockedOut || !user.PasswordChanged ||
            string.IsNullOrWhiteSpace(user.PasswordHash)) return Unauthorized();
        return user.UserRole is Role.Admin or Role.SuperAdmin
                ? Ok(new { user.Email, Role = user.UserRole.ToString() })
                : StatusCode(403);
    }
}
