using System.Security.Claims;
using CLMS.Application.Authentication.Services;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;

namespace CLMS.Web.Controllers;

[ApiController]
// TODO : Remember to remove once cookie authentication is yes or when
//      done testing api
[IgnoreAntiforgeryToken]
public class AuthController(
    CLMS.Application.Authentication.Services.AuthenticationService authService)
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
        
            var identity = new ClaimsIdentity(claims, "cookie");
            
            var principal = new ClaimsPrincipal(identity);
            
            await HttpContext.SignInAsync("cookie",principal);

            return Redirect("/");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Redirect("/login?error=invalid");
        }
    }
    
    [HttpPost("/auth/cookie-logout")]
    
    
    
    /// <summary>
    /// Logs in using jwt
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
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

    [HttpPost("api/auth/logout")]
    public async Task<IActionResult> Logout([FromBody] string refreshToken)
    {
        await authService.Logout(refreshToken);
        return Ok("Logout successful");
    }

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