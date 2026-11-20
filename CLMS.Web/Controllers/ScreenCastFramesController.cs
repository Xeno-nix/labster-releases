using CLMS.Web.Services.ScreenCasting;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/screen-cast")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Roles = "Admin,Faculty")]
public sealed class ScreenCastFramesController(ScreenCastService screenCasts) : ControllerBase
{
    [HttpGet("{sessionId:guid}/frame")]
    public async Task<IActionResult> Frame(Guid sessionId, [FromQuery] long after = 0, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!await screenCasts.ValidateSessionAgentAsync(User, sessionId, cancellationToken)) return NotFound();
        if (!screenCasts.TryGetFrame(User, sessionId, Math.Max(0, after), out var frame)) return NotFound();
        if (frame is null) return NoContent();
        Response.Headers["X-Frame-Sequence"] = frame.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return File(frame.JpegBytes, "image/jpeg");
    }
}
