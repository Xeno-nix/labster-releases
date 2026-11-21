using CLMS.Application.Authorization;
using CLMS.Domain.Enums;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController, Route("api/admin/authorization")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PrivilegeManagementController(PrivilegeManagementService privileges) : ControllerBase
{
    [HttpGet("roles"), Authorize(Policy = Privileges.Prefix + Privileges.SecurityManage)]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await privileges.RolesAsync(ct));
    [HttpPut("roles/{role}/privileges"), ValidateAntiForgeryToken, Authorize(Policy = Privileges.Prefix + Privileges.SecurityManage)]
    public async Task<IActionResult> RolePrivileges(Role role, RolePrivilegesRequest request, CancellationToken ct)
    {
        if (request.Privileges is null || request.Privileges.Length > Privileges.All.Count) return BadRequest();
        try { await privileges.SetRoleAsync(role, request.Privileges, ct); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
    [HttpGet("users/{id:guid}/privileges"), Authorize(Policy = Privileges.Prefix + Privileges.UsersView)]
    public async Task<IActionResult> UserPrivileges(Guid id, CancellationToken ct)
    {
        var access = await privileges.UserAsync(id, ct);
        return access is null ? NotFound() : Ok(new { access.UserId, access.Role, access.IsActive, access.Inherited, access.Overrides, access.Effective });
    }
    [HttpPut("users/{id:guid}/overrides"), ValidateAntiForgeryToken, Authorize(Policy = Privileges.Prefix + Privileges.UsersManagePrivileges)]
    public async Task<IActionResult> Override(Guid id, PrivilegeOverrideRequest request, CancellationToken ct)
    {
        try { await privileges.SetOverrideAsync(id, request.Privilege, request.IsGranted, ct); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
public sealed record RolePrivilegesRequest(string[] Privileges);
public sealed record PrivilegeOverrideRequest(string Privilege, bool? IsGranted);
