using CLMS.Application.Logging.Dtos;
using CLMS.Application.UnitSessions;
using CLMS.Domain.Common;
using CLMS.Web.Auth;

namespace CLMS.Web.Endpoints;

/// <summary>
/// Agent-facing endpoints. No business logic here — each just stamps the server
/// clock and dispatches to the Application handler. Serial numbers come from the
/// API-key-authenticated principal, never from the request body.
/// </summary>
public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/agent")
            .RequireAuthorization("AgentPolicy");

        group.MapPost("/session-started", HandleAsync);
        group.MapPost("/session-ended", HandleAsync);
        group.MapPost("/heartbeat", HandleAsync);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        UnitSessionStartedHandler startedHandler,
        UnitSessionEndedHandler endedHandler,
        UnitHeartbeatHandler heartbeatHandler,
        CancellationToken cancellationToken)
    {
        var serial = context.User.FindFirst(AgentApiKeyAuthenticationHandler.UnitSerialClaim)?.Value;
        if (string.IsNullOrEmpty(serial))
            return Results.Unauthorized();

        var occurredAtUtc = DateTime.UtcNow;
        var path = context.Request.Path.Value?.ToLowerInvariant();

        try
        {
            LoggingResult result = path switch
            {
                "/api/agent/session-started" =>
                    await startedHandler.HandleAsync(new UnitSessionStartedCommand(serial, occurredAtUtc)),
                "/api/agent/session-ended" =>
                    await endedHandler.HandleAsync(new UnitSessionEndedCommand(serial, occurredAtUtc)),
                "/api/agent/heartbeat" =>
                    await heartbeatHandler.HandleAsync(new UnitHeartbeatCommand(
                        serial, occurredAtUtc, context.Connection.RemoteIpAddress?.ToString())),
                _ => LoggingResult.Failed("Unknown agent endpoint."),
            };

            return result.Success
                ? Results.Ok(result)
                : Results.Problem(title: "Agent event rejected", detail: result.Message);
        }
        catch (DomainException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }
}
