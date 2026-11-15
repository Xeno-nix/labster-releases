using System.Security.Claims;
using CLMS.Application.Identity.Services;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using CLMS.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using ConfirmationPage = CLMS.Web.Components.Pages.Account.TwoFactorConfirmation;

namespace CLMS.Web.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[EnableRateLimiting("GoogleLink")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TwoFactorSettingsController(TwoFactorSettingsService settings) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Frame-Options"] = "DENY";
        base.OnActionExecuting(context);
    }

    [HttpPost("/account/two-factor/request"), ValidateAntiForgeryToken]
    public async Task<IResult> RequestChange([FromForm] bool enabled, CancellationToken ct)
    {
        try { return Page(new(await settings.RequestAsync(UserId(), Stamp(), enabled, ct))); }
        catch (InvalidOperationException ex) { return Page(new(null, Error: ex.Message)); }
        catch (UnauthorizedAccessException) { return Results.Redirect("/login"); }
    }

    [HttpPost("/account/two-factor/confirm"), ValidateAntiForgeryToken]
    public async Task<IResult> Confirm([FromForm] ConfirmTwoFactorSettingDto request, CancellationToken ct)
    {
        try
        {
            var original = await HttpContext.AuthenticateAsync(AuthenticationSchemes.Cookie);
            if (!original.Succeeded || original.Properties is null) return Results.Redirect("/login");
            var confirmed = await settings.ConfirmAsync(UserId(), Stamp(), request, ct);
            if (!confirmed.TwoFactorEnabled)
            {
                await HttpContext.SignOutAsync(AuthenticationSchemes.PasswordChange);
                await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
                await BrowserSignIn.CancelPendingAsync(HttpContext);
                await BrowserSignIn.RenewAfterDisablingAsync(HttpContext, confirmed, original.Properties);
                return Results.Redirect("/account/settings?message=two_factor_disabled");
            }
            foreach (var scheme in new[] { AuthenticationSchemes.Cookie, AuthenticationSchemes.PasswordChange, AuthenticationSchemes.ExternalCookie })
                await HttpContext.SignOutAsync(scheme);
            await BrowserSignIn.CancelPendingAsync(HttpContext);
            return Results.Redirect("/login?message=two_factor_updated");
        }
        catch (InvalidOperationException) { return await Failure(request.ChallengeCredential, "The password or code is incorrect, or the request expired. Retry or return to account settings."); }
        catch (UnauthorizedAccessException) { return Results.Redirect("/login"); }
    }

    [HttpPost("/account/two-factor/resend"), ValidateAntiForgeryToken]
    public async Task<IResult> Resend([FromForm] string credential, CancellationToken ct)
    {
        try { return Page(new(await settings.ResendAsync(UserId(), Stamp(), credential, ct), Message: "A replacement code was sent.")); }
        catch (InvalidOperationException ex) { return await Failure(credential, ex.Message); }
        catch (UnauthorizedAccessException) { return Results.Redirect("/login"); }
    }

    [HttpPost("/account/two-factor/cancel"), ValidateAntiForgeryToken]
    public async Task<IResult> Cancel([FromForm] string credential)
    {
        try { await settings.CancelAsync(UserId(), Stamp(), credential); }
        catch (InvalidOperationException) { }
        catch (UnauthorizedAccessException) { return Results.Redirect("/login"); }
        return Results.Redirect("/account/settings");
    }

    private async Task<IResult> Failure(string credential, string message)
    {
        try { return Page(new(await settings.PendingAsync(UserId(), Stamp(), credential), Error: message)); }
        catch (InvalidOperationException) { return Page(new(null, Error: message)); }
        catch (UnauthorizedAccessException) { return Results.Redirect("/login"); }
    }
    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Stamp() => User.FindFirstValue("security_stamp") ?? "";
    private static IResult Page(TwoFactorConfirmationModel model) => new RazorComponentResult<ConfirmationPage>(new { Model = model }) { PreventStreamingRendering = true };
}
