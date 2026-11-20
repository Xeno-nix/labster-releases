using System.Security.Claims;
using CLMS.Application.Identity.Services;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using CLMS.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using SecurityPage = CLMS.Web.Components.Pages.Account.SecurityForm;
using AuthenticationService = CLMS.Application.Identity.Services.AuthenticationService;

namespace CLMS.Web.Controllers;

[EnableRateLimiting("GoogleLink")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountSecurityController(EmailLoginService login, PasswordRecoveryService recovery, AuthenticationService authentication) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Frame-Options"] = "DENY";
        base.OnActionExecuting(context);
    }
    [HttpGet("/forgot-password")]
    public IResult Forgot() => Page(new("forgot"));
    [HttpPost("/forgot-password"), ValidateAntiForgeryToken, EnableRateLimiting("PasswordRecovery")]
    public IResult Forgot([FromForm] string email)
    {
        try
        {
            var result = recovery.Request(email);
            if (result.RateLimited)
            {
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((result.RetryAt - result.ServerTime).TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return Page(new("forgot", Message: "If the account is eligible, a reset link will be emailed. Check your inbox and spam folder.")
            {
                Email = email.Trim().ToLowerInvariant(), RecoveryRetryAt = result.RetryAt, RecoveryServerTime = result.ServerTime
            });
        }
        catch (InvalidOperationException) { return Page(new("forgot", Error: "Enter a valid email address, or contact support if recovery is unavailable.")); }
    }
    [HttpGet("/reset-password")]
    public IResult Reset(Guid userId, string? token) => Page(new("reset", UserId: userId, Token: token is { Length: 43 } ? token : ""));
    [HttpPost("/reset-password"), ValidateAntiForgeryToken, EnableRateLimiting("PasswordRecovery")]
    public async Task<IResult> Reset([FromForm] ResetPasswordDto request, CancellationToken ct)
    {
        try
        {
            await recovery.ResetAsync(request, ct);
            foreach (var scheme in new[] { AuthenticationSchemes.Cookie, AuthenticationSchemes.PasswordChange, AuthenticationSchemes.ExternalCookie })
                await HttpContext.SignOutAsync(scheme);
            await BrowserSignIn.CancelPendingAsync(HttpContext);
            return Results.Redirect("/login?message=password_reset");
        }
        catch (InvalidOperationException ex) { return Page(new("reset", Error: ex.Message, UserId: request.UserId, Token: request.Token)); }
    }
    [HttpGet("/auth/two-factor")]
    public async Task<IResult> TwoFactor()
    {
        var pending = await PendingAsync();
        return pending is null ? Results.Redirect("/login?error=verification_expired") : Page(Model(pending));
    }
    [HttpPost("/auth/two-factor"), ValidateAntiForgeryToken]
    public async Task<IResult> Verify([FromForm] string code)
    {
        var pending = await PendingAsync();
        if (pending is null) return Results.Redirect("/login?error=verification_expired");
        try
        {
            var verified = await login.VerifyAsync(new(Credential(pending), code));
            return Results.LocalRedirect(await BrowserSignIn.CompleteAsync(HttpContext, verified, pending.Principal!.FindFirstValue("remember") == "True"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        { return Page(Model(pending) with { Error = "The code is incorrect or the request expired. Retry or start sign-in again." }); }
    }
    [HttpPost("/auth/two-factor/resend"), ValidateAntiForgeryToken]
    public async Task<IResult> Resend(CancellationToken ct)
    {
        var pending = await PendingAsync();
        if (pending is null) return Results.Redirect("/login?error=verification_expired");
        try
        {
            var receipt = await login.ResendAsync(Credential(pending), ct);
            return Page(Model(pending) with { ResendAfter = receipt.ResendAfter, Message = "A replacement code was sent." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        { return Page(Model(pending) with { Error = ex is UnauthorizedAccessException ? "Sign in again." : ex.Message }); }
    }
    [HttpPost("/auth/two-factor/cancel"), ValidateAntiForgeryToken]
    public async Task<IResult> Cancel() { await BrowserSignIn.CancelPendingAsync(HttpContext); return Results.Redirect("/login"); }
    [HttpPost("/api/auth/two-factor/verify"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> ApiVerify([FromBody] VerifyLoginCodeDto request)
    {
        if (!ModelState.IsValid || request is null) return BadRequest(new { code = "invalid_verification" });
        try { return Ok(await authentication.IssueTokens(await login.VerifyAsync(request))); }
        catch (PasswordChangeRequiredException) { return StatusCode(403, new { code = "password_change_required", passwordChangeUrl = "/account/change-password" }); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return Unauthorized(new { code = "invalid_verification" }); }
    }
    [HttpPost("/api/auth/two-factor/resend"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> ApiResend([FromBody] ResendLoginCodeDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request is null) return BadRequest(new { code = "verification_unavailable" });
        try { return Ok(await login.ResendAsync(request.ChallengeCredential, ct)); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return BadRequest(new { code = "verification_unavailable" }); }
    }
    private async Task<AuthenticateResult?> PendingAsync()
    {
        var ticket = await HttpContext.AuthenticateAsync(AuthenticationSchemes.TwoFactor);
        if (!ticket.Succeeded || ticket.Principal?.FindFirstValue("pending_credential") is not { Length: 43 }) return null;
        try { login.Status(Credential(ticket)); return ticket; } catch (InvalidOperationException) { return null; }
    }
    private SecurityFormModel Model(AuthenticateResult ticket)
    {
        try { var r = login.Status(Credential(ticket)); return new("verify", ExpiresAt: r.ExpiresAt, ResendAfter: r.ResendAfter); }
        catch (InvalidOperationException) { return new("verify", ExpiresAt: ticket.Properties?.ExpiresUtc?.UtcDateTime); }
    }
    private static string Credential(AuthenticateResult ticket) => ticket.Principal!.FindFirstValue("pending_credential")!;
    private static IResult Page(SecurityFormModel model) => new RazorComponentResult<SecurityPage>(new { Model = model }) { PreventStreamingRendering = true };
}
