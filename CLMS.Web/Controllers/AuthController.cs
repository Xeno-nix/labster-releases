using CLMS.Application.Identity.Services;
using System.Security.Claims;
using CLMS.Shared.Contracts;
using CLMS.Application.Identity.Exceptions;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AuthenticationService = CLMS.Application.Identity.Services.AuthenticationService;

namespace CLMS.Web.Controllers;

[ApiController]
public class AuthController(AuthenticationService authService) : ControllerBase
{

    [HttpPost("/auth/cookie-login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("GoogleLink")]
    public async Task<IActionResult> CookieLogin(
        [FromForm] AuthenticationRequestDto request,
        [FromForm] bool rememberMe = false)
    {
        try
        {
            var user = await authService.AuthenticateUserAsync(request);
            return LocalRedirect(await BrowserSignIn.CompleteAsync(HttpContext, user, rememberMe));
        }
        catch (AccountLockedException ex)
        {
            var minutesLeft = (int)Math.Ceiling((ex.LockoutEnd - DateTime.UtcNow).TotalMinutes);
            return Redirect(
                $"/login?error=locked&minutes={Math.Max(minutesLeft, 1)}"
            );
        }
        catch (InvalidCredentialsException ex) when (ex.AttemptsRemaining is not null)
        { return Redirect($"/login?error=invalid&attemptsLeft={ex.AttemptsRemaining}"); }
        catch (UnauthorizedAccessException)
        { return Redirect("/login?error=invalid"); }
        catch (InvalidOperationException)
        { return Redirect("/login?error=invalid"); }
    }

    
    [HttpPost("/auth/cookie-logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.Cookie);
        await HttpContext.SignOutAsync(AuthenticationSchemes.PasswordChange);
        await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
        return Redirect("/login");
    }

    [IgnoreAntiforgeryToken]
    [HttpPost("api/auth/login")]
    [EnableRateLimiting("GoogleLink")]
    public async Task<IActionResult> Login([FromBody] AuthenticationRequestDto request)
    {
        try { return Ok(await authService.JwtLogin(request)); }
        catch (PasswordChangeRequiredException) { return PasswordChangeRequired(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [IgnoreAntiforgeryToken]
    [HttpPost("api/auth/register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto request)
    {
        RegistrationResultDto result;
        try
        {
            result = await authService.RegisterAsync(request);
            // result = await authService.JwtLogin(new(request.Email,request.Password));
        }
        catch (InvalidOperationException ioex)
        {
            return Conflict(ioex.Message);
        } 
        catch (UnauthorizedAccessException uaex)
        {
            return Unauthorized(uaex.Message);
        }
        
        // login success. Returns access and refresh token.
        return Ok(result);
    }

    [IgnoreAntiforgeryToken]
    [HttpPost("api/auth/logout")]
    public async Task<IActionResult> Logout([FromBody] string refreshToken)
    {
        await authService.Logout(refreshToken);
        return Ok("Logout successful");
    }

    [IgnoreAntiforgeryToken]
    [HttpPost("api/auth/refresh")]
    public async Task<IActionResult> Refresh([FromBody] string refreshToken)
    {
        try { return Ok(await authService.Refresh(refreshToken)); }
        catch (PasswordChangeRequiredException) { return PasswordChangeRequired(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(ex.Message); }
    }

    private ObjectResult PasswordChangeRequired() => StatusCode(StatusCodes.Status403Forbidden,
        new { code = "password_change_required", passwordChangeUrl = "/account/change-password",
            message = "Sign in through the browser and change your initial password before using the API." });

    [AllowAnonymous]
    [HttpGet("auth/google/start")]
    public async Task<IActionResult> GoogleStart(string? returnUrl = "/")
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), "Auth", new { returnUrl })
        }, AuthenticationSchemes.Google);
    }

    [AllowAnonymous]
    [HttpGet("auth/google/callback")]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = "/")
    {
        var ticket = await HttpContext.AuthenticateAsync(AuthenticationSchemes.ExternalCookie);
        if (!ticket.Succeeded || ticket.Principal is null) return Redirect("/login?error=google_verification_failed");
        try
        {
            var user = await authService.AuthenticateExternalUserAsync(BrowserSignIn.GoogleIdentity(ticket.Principal));
            if (user.RequiresLink)
            {
                // Preserve the verified provider ticket for a five-minute password confirmation.
                // Form inputs cannot select the internal user or supply a Google subject.
                var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) };
                properties.Items["google-link-pending"] = "true";
                await HttpContext.SignInAsync(AuthenticationSchemes.ExternalCookie, ticket.Principal, properties);
                return Redirect("/auth/google/link");
            }
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            var path = await BrowserSignIn.CompleteAsync(HttpContext, new(user.UserId, user.Email, user.UserRole, user.PasswordChanged));
            return LocalRedirect(path == "/" && Url.IsLocalUrl(returnUrl) ? returnUrl! : path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            return Redirect("/login?error=google_account_not_authorized");
        }
    }
    
}
