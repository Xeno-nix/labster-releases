using CLMS.Application.Authentication.Services;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
// TODO : Remember to remove once cookie authentication is yes or when
//      done testing api
[IgnoreAntiforgeryToken]
public class AuthController(
    AuthenticationService authService)
    : ControllerBase
{
    
    /// <summary>
    /// Logs in using jwt
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost("login")]
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

    [HttpPost("register")]
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

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] string refreshToken)
    {
        await authService.Logout(refreshToken);
        return Ok("Logout successful");
    }

    [HttpPost("refresh")]
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