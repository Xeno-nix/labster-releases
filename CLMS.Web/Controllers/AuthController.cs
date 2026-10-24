using System.Security.Claims;
using CLMS.Shared.Contracts;
using CLMS.Application.Identity.Exceptions;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using AuthenticationService = CLMS.Application.Identity.Services.AuthenticationService;

namespace CLMS.Web.Controllers;

[ApiController]
public class AuthController(
    AuthenticationService authService)
    : ControllerBase
{
    [HttpPost("/auth/cookie-login")]
    public async Task<IActionResult> CookieLogin([FromForm] AuthenticationRequestDto loginRequest, [FromForm] bool rememberMe = false)
    {
        UserClaimsDto result;
        try
        {
            result = await authService.AuthenticateUserAsync(loginRequest);
        
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
                new(ClaimTypes.Email, result.Email),
                new(ClaimTypes.Role, result.UserRole.ToString())
            };

            var properties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(
                    rememberMe
                        ? TimeSpan.FromDays(5)
                        : TimeSpan.FromHours(8)
                    )
            };
        
            var identity = new ClaimsIdentity(claims, AuthenticationSchemes.Cookie);
            
            var principal = new ClaimsPrincipal(identity);
            
            await HttpContext.SignInAsync(AuthenticationSchemes.Cookie,principal, properties);

            return Redirect("/");
        }
        catch (AccountLockedException ex)
        {
            var minutesLeft = (int)Math.Ceiling((ex.LockoutEnd - DateTime.UtcNow).TotalMinutes);
            return Redirect($"/login?error=locked&minutes={Math.Max(minutesLeft, 1)}");
        }
        catch (InvalidCredentialsException ex) when (ex.AttemptsRemaining is not null)
        {
            return Redirect($"/login?error=invalid&attemptsLeft={ex.AttemptsRemaining}");
        }
        catch (UnauthorizedAccessException)
        {
            return Redirect("/login?error=invalid");
        }
    }
    
    [HttpPost("/auth/cookie-logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.Cookie);

        return Redirect("/");
    }
    
    
    /// <summary>
    /// Logs in using jwt
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [IgnoreAntiforgeryToken]
    [HttpPost("api/auth/login")]
    public async Task<IActionResult> Login([FromBody] AuthenticationRequestDto request)
    {
        // checks credential validity
        AuthTokensDto result;
        try
        {
            result = await authService.JwtLogin(request);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
        
        // login success. Returns access and refresh token.
        return Ok(result);
    }

    [IgnoreAntiforgeryToken]
    [HttpPost("api/auth/register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto request)
    {
        AuthTokensDto result;
        try
        {
            await authService.RegisterAsync(request);
            result = await authService.JwtLogin(new(request.Email,request.Password));
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
        AuthTokensDto result;
        try
        {
            result = await authService.Refresh(refreshToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
        
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("auth/google/start")]
    public IActionResult GoogleStart(string? returnUrl = "/")
    {
        if (!Url.IsLocalUrl(returnUrl))
        {
            returnUrl = "/";
        }
        
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(
                nameof(GoogleCallback),
                "Auth",
                new { returnUrl })
        };

        return Challenge(properties, AuthenticationSchemes.Google);
    }

    [HttpGet("auth/google/callback")]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = "/")
    {
        var externalAuth = await HttpContext.AuthenticateAsync(
            AuthenticationSchemes.ExternalCookie);
        
        // login failed
        if (!externalAuth.Succeeded ||
            externalAuth.Principal is null)
        {
            return Redirect("/login?error=external_login_failed");
        }
        
        // if login success
        
        // stored set of external credentials
        var principal = externalAuth.Principal;

        // claimSub or the external id
        var subject = principal.FindFirstValue(
            ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(
            ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);

            return Redirect("/login?error=external_claims_missing");
        }

        try
        {
            // after retrieving external id and email
            // authenticate external user 
            var user = await authService
                .AuthenticateExternalUserAsync(
                    new (
                        "google",
                        subject,
                        email)
                    );
            
            // create claims for cookie sign in
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier,
                    user.UserId.ToString()),
                new(ClaimTypes.Email, user.Email)
            };
            
            claims.Add(new Claim(ClaimTypes.Role, user.UserRole.ToString()));

            var clmsIdentity = new ClaimsIdentity(
                claims,
                AuthenticationSchemes.Cookie);

            var clmsPrincipal = new ClaimsPrincipal(clmsIdentity);
            
            await HttpContext.SignInAsync(
                AuthenticationSchemes.Cookie,
                clmsPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true
                });

            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);

            if (!Url.IsLocalUrl(returnUrl))
            {
                returnUrl = "/";
            }

            return LocalRedirect(returnUrl!);
        }
        catch (UnauthorizedAccessException)
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            return Redirect("/login?error=google_account_not_authorized");
        }
    }
    
}
