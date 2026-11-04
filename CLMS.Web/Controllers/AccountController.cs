using System.Security.Claims;
using CLMS.Application.Identity.Services;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using CLMS.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using ChangePasswordPage = CLMS.Web.Components.Pages.Account.ChangePassword;
using GoogleLinkPage = CLMS.Web.Components.Pages.Account.GoogleLink;
using AuthenticationService = CLMS.Application.Identity.Services.AuthenticationService;

namespace CLMS.Web.Controllers;

[EnableRateLimiting("GoogleLink")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(AuthenticationService accounts) : Controller
{
    private const string PasswordSchemes = AuthenticationSchemes.PasswordChange + "," + AuthenticationSchemes.Cookie;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Frame-Options"] = "DENY";
        base.OnActionExecuting(context);
    }

    [Authorize(AuthenticationSchemes = PasswordSchemes)]
    [HttpGet("/account/change-password")]
    public async Task<IResult> ChangePassword()
    {
        try
        {
            var user = await accounts.GetActiveUserAsync(UserId());
            return new RazorComponentResult<ChangePasswordPage>(new { Model = new AccountFormViewModel(user.Email, !user.PasswordChanged) })
                { PreventStreamingRendering = true };
        }
        catch (UnauthorizedAccessException) { return await EndSession(); }
    }

    [Authorize(AuthenticationSchemes = PasswordSchemes)]
    [HttpPost("/account/change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> ChangePassword([FromForm] ChangePasswordDto request)
    {
        try
        {
            await accounts.ChangePasswordAsync(UserId(), request);
            await HttpContext.SignOutAsync(AuthenticationSchemes.Cookie);
            await HttpContext.SignOutAsync(AuthenticationSchemes.PasswordChange);
            return Results.Redirect("/login?message=password_changed");
        }
        catch (InvalidOperationException ex)
        {
            var user = await accounts.GetActiveUserAsync(UserId());
            return new RazorComponentResult<ChangePasswordPage>(new { Model = new AccountFormViewModel(user.Email, !user.PasswordChanged, ex.Message) })
                { PreventStreamingRendering = true };
        }
        catch (UnauthorizedAccessException) { return await EndSession(); }
    }

    [Authorize(AuthenticationSchemes = PasswordSchemes)]
    [HttpPost("/account/change-password/cancel")]
    [ValidateAntiForgeryToken]
    public Task<IResult> CancelPasswordChange() => EndSession();

    [AllowAnonymous]
    [HttpGet("/auth/google/link")]
    public async Task<IResult> GoogleLink()
    {
        var ticket = await PendingGoogleTicket();
        if (ticket is null) return Results.Redirect("/login?error=google_verification_failed");
        try
        {
            var user = await accounts.AuthenticateExternalUserAsync(BrowserSignIn.GoogleIdentity(ticket.Principal!));
            return new RazorComponentResult<GoogleLinkPage>(new { Model = new AccountFormViewModel(user.Email) })
                { PreventStreamingRendering = true };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            return Results.Redirect("/login?error=google_account_not_authorized");
        }
    }

    [AllowAnonymous]
    [HttpPost("/auth/google/link")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> GoogleLink([FromForm] string password)
    {
        var ticket = await PendingGoogleTicket();
        if (ticket is null) return Results.Redirect("/login?error=google_verification_failed");
        var identity = BrowserSignIn.GoogleIdentity(ticket.Principal!);
        try
        {
            var user = await accounts.LinkGoogleAsync(identity, password);
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            return Results.LocalRedirect(await BrowserSignIn.CompleteAsync(HttpContext, new(user.UserId, user.Email, user.UserRole, user.PasswordChanged)));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            // The Google cookie remains available for a retry, but no normal Labster login is created.
            return new RazorComponentResult<GoogleLinkPage>(new { Model = new AccountFormViewModel(identity.Email, Error: ex.Message) })
                { PreventStreamingRendering = true };
        }
    }

    [AllowAnonymous]
    [HttpPost("/auth/google/link/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> CancelGoogleLink()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
        return Results.Redirect("/login");
    }

    private async Task<AuthenticateResult?> PendingGoogleTicket()
    {
        var ticket = await HttpContext.AuthenticateAsync(AuthenticationSchemes.ExternalCookie);
        return ticket.Succeeded && ticket.Principal is not null &&
            ticket.Properties?.Items.TryGetValue("google-link-pending", out var pending) == true && pending == "true" ? ticket : null;
    }

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UnauthorizedAccessException("Sign in again.");

    private async Task<IResult> EndSession()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.Cookie);
        await HttpContext.SignOutAsync(AuthenticationSchemes.PasswordChange);
        return Results.Redirect("/login");
    }
}
