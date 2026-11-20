using System.Security.Claims;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Infrastructure.Repositories;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CLMS.Web.Controllers;

[ApiController]
[Route("api/agent/workstation-sessions")]
public sealed class WorkstationSessionsController(WorkstationSessionRepository sessions,
    WorkstationSessionGrants grants, HumanSessionValidator humans) : ControllerBase
{
    [HttpPost("grant")]
    [Authorize(Policy = "JwtPolicy")]
    public IActionResult Grant(WorkstationSessionGrantRequest request)
    {
        if (request.UnitId == Guid.Empty || request.SessionId == Guid.Empty)
            return BadRequest();
        if (User.FindFirstValue("principal_type") != "human" ||
            !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            User.FindFirstValue("security_stamp") is not { Length: > 0 } stamp)
            return Forbid(AuthenticationSchemes.Bearer);

        return Ok(new WorkstationSessionGrantResponse(grants.Issue(request.UnitId, request.SessionId, userId, stamp)));
    }

    [HttpPut("{sessionId:guid}")]
    [Authorize(Policy = "AgentOnly")]
    public async Task<IActionResult> Start(Guid sessionId, WorkstationSessionStartRequest request, CancellationToken ct)
    {
        if (sessionId == Guid.Empty) return BadRequest();
        var unitId = AgentUnitId();
        var identity = grants.Read(request.Grant, unitId, sessionId);
        if (identity is null) return Forbid(AuthenticationSchemes.AgentBearer);

        var human = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
                new Claim("principal_type", "human"),
                new Claim("security_stamp", identity.SecurityStamp)
            ], AuthenticationSchemes.Bearer));
        if (!await humans.IsCurrentAsync(human)) return Forbid(AuthenticationSchemes.AgentBearer);

        return Result(await sessions.StartAsync(sessionId, unitId, identity.UserId, ct));
    }

    [HttpPut("{sessionId:guid}/end")]
    [Authorize(Policy = "AgentOnly")]
    public async Task<IActionResult> End(Guid sessionId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty) return BadRequest();
        return Result(await sessions.EndAsync(sessionId, AgentUnitId(), ct));
    }

    private Guid AgentUnitId() => Guid.Parse(User.FindFirstValue("computer_unit_id")!);

    private IActionResult Result(WorkstationSessionWrite result) => result switch
    {
        WorkstationSessionWrite.Accepted => NoContent(),
        WorkstationSessionWrite.NotFound => NotFound(),
        WorkstationSessionWrite.Ended => Conflict(new { code = "workstation_session_ended" }),
        _ => Conflict(new { code = "workstation_session_conflict" })
    };
}

public sealed record WorkstationSessionGrantRequest(Guid UnitId, Guid SessionId);
public sealed record WorkstationSessionGrantResponse(string Grant);
public sealed record WorkstationSessionStartRequest(string Grant);
