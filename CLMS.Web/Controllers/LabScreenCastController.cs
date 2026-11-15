using CLMS.Web.Services.ScreenCasting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/screen-cast/labs")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = "Admin,Faculty")]
public sealed class LabScreenCastController(LabScreenCastService sessions) : ControllerBase
{
    [HttpGet("{id:int}/lease")]
    public async Task<IActionResult> Lease(int id)
    {
        Response.Headers.CacheControl = "no-store, private";
        return await sessions.RenewAsync(User, id) ? NoContent() : NotFound();
    }
}
