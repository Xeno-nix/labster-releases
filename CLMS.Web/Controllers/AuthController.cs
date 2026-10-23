using System.Security.Claims;
using CLMS.Application.Identity.Exceptions;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using AuthenticationService = CLMS.Application.Identity.Services.AuthenticationService;

namespace CLMS.Web.Controllers;

[ApiController]
public class AuthController(
    AuthenticationService authService)
    : ControllerBase
{
    [HttpPost("/auth/cookie-login")]
    public async Task<IActionResult> CookieLogin([FromForm] AuthenticationRequestDto loginRequest)
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
        
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            
            var principal = new ClaimsPrincipal(identity);
            
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,principal);

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
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

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
}