using CLMS.Application.Authentication.UseCases;
using CLMS.Shared.AuthRequests;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using LoginRequest = CLMS.Shared.AuthRequests.LoginRequest;
using RefreshRequest = Microsoft.AspNetCore.Identity.Data.RefreshRequest;
using RegisterRequest = CLMS.Shared.AuthRequests.RegisterRequest;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
// Disable below when pushing to prod
[IgnoreAntiforgeryToken]
public class AuthController : ControllerBase
{
    private readonly LoginUseCase _loginUseCase;
    private readonly LogoutUseCase _logoutUseCase;
    private readonly RegisterUseCase _registerUseCase;
    private readonly RefreshTokenUseCase _refreshTokenUseCase;

    public AuthController(LoginUseCase loginUseCase,
        RegisterUseCase registerUseCase,
        RefreshTokenUseCase refreshTokenUseCase,
        LogoutUseCase logoutUseCase)
    {
        _loginUseCase = loginUseCase;
        _refreshTokenUseCase = refreshTokenUseCase;
        _logoutUseCase = logoutUseCase;
        _registerUseCase = registerUseCase;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // checks credential validity
        AuthenticationResponse result;
        try
        {
            result = await _loginUseCase.ExecuteAsync(request);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
        
        // login success. Returns access and refresh token.
        return Ok(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        AuthenticationResponse result;
        try
        {
            await _registerUseCase.ExecuteAsync(request);
            result = await _loginUseCase.ExecuteAsync(new(request.Email, request.Password));
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
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        await  _logoutUseCase.ExecuteAsync(request.RefreshToken);
        return Ok("Logout successful");
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        AuthenticationResponse result;
        try
        {
            result = await _refreshTokenUseCase.ExecuteAsync(request.RefreshToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(ex.Message);
        }
        
        return Ok(result);
    }
}