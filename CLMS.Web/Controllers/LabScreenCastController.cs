using CLMS.Application.Authorization;
using CLMS.Web.Authentication;
using CLMS.Web.Services.ScreenCasting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/screen-cast/labs")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Privileges.Prefix + Privileges.MonitoringLabScreens)]
public sealed class LabScreenCastController(LabScreenCastService sessions) : ControllerBase
{
    [HttpGet("{id:int}/lease")]
    public async Task<IActionResult> Lease(int id)
    {
        Response.Headers.CacheControl = "no-store, private";
        return await sessions.RenewAsync(User, id) ? NoContent() : NotFound();
    }
}
