using System.Security.Claims;
using CLMS.Shared.Dtos;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using CLMS.Web.Services.ScreenCasting;

namespace CLMS.Web.Hubs;

[Authorize(Policy = "AgentOnly")]
public sealed class AgentConnectionHub(ILogger<AgentConnectionHub> logger, ScreenCastService screenCasts,
    AgentPresenceStore presence, TimeProvider clock, FacultyStreamService facultyStreams) : Hub
{
    private Guid UnitId => Guid.Parse(Context.User!.FindFirstValue("computer_unit_id")!);

    public Task AnswerFacultyStream(FacultyStreamAnswer answer) =>
        facultyStreams.AnswerAsync(UnitId, Context.ConnectionId, answer);

    public Task CloseFacultyStream(Guid streamId, Guid peerId) =>
        facultyStreams.ClosedAsync(UnitId, Context.ConnectionId, streamId, peerId);

    public bool PublishScreenFrame(ScreenCastFrame frame) => screenCasts.Publish(UnitId, Context.ConnectionId, frame);

    public void ScreenCaptureStopped(Guid sessionId) => screenCasts.CaptureStopped(UnitId, Context.ConnectionId, sessionId);

    public void ReportNetworkResources(NetworkResourceReading? reading)
    {
        if (reading is { IsValid: false }) throw new HubException("Invalid network resource reading.");
        presence.ReportNetworkResources(UnitId, Context.ConnectionId, reading);
    }
    // The unit identity comes from the validated key, never from a client-supplied unit ID.
    public AgentConnectionInfo Heartbeat()
    {
        presence.Heartbeat(UnitId, Context.ConnectionId);
        return new(UnitId, Context.ConnectionId, clock.GetUtcNow());
    }

    public override async Task OnConnectedAsync()
    {
        await screenCasts.AgentConnectedAsync(UnitId, Context.ConnectionId);
        presence.Connected(UnitId, Context.ConnectionId, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());
        logger.LogInformation("Agent connected for unit {ComputerUnitId}.",
            UnitId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await screenCasts.AgentDisconnectedAsync(UnitId, Context.ConnectionId);
        presence.Disconnected(UnitId, Context.ConnectionId);
        logger.LogInformation("Agent disconnected for unit {ComputerUnitId}.",
            UnitId);
        await base.OnDisconnectedAsync(exception);
    }
}
