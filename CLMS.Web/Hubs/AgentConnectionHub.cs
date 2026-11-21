using System.Security.Claims;
using CLMS.Shared.Dtos;
using CLMS.Web.Services.AgentConnections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using CLMS.Web.Services.ScreenCasting;

namespace CLMS.Web.Hubs;

[Authorize(Policy = "AgentOnly")]
public sealed class AgentConnectionHub(ILogger<AgentConnectionHub> logger, ScreenCastService screenCasts,
    AgentPresenceStore presence, TimeProvider clock, ApplicationScanCoordinator applications,
    CLMS.Application.Monitoring.IApplicationRegistryRepository registry) : Hub
{
    private Guid UnitId => Guid.Parse(Context.User!.FindFirstValue("computer_unit_id")!);

    public async Task<AgentApplicationRegistry> GetApplicationRegistry(CLMS.Domain.Entities.UnitManagement.ApplicationPlatform platform)
    {
        if (presence.Snapshot().GetValueOrDefault(UnitId)?.ConnectionId != Context.ConnectionId)
            throw new HubException("This agent connection has been replaced.");
        applications.Register(UnitId, Context.ConnectionId, platform);
        var all = await registry.GetAllAsync(Context.ConnectionAborted);
        var entries = all
            .Where(a => a.IsEnabled && CLMS.Domain.Entities.UnitManagement.ApplicationProcessNames.AppliesTo(a.Platform, platform))
            .Select(a => new AgentApplication(a.Id, a.ProcessName, a.Platform)).ToArray();
        return new(CLMS.Application.Monitoring.ApplicationRegistryRevision.Compute(all), entries);
    }

    public bool PublishApplicationScan(ApplicationScanResponse response) =>
        applications.Publish(UnitId, Context.ConnectionId, response);

    public bool PublishScreenFrame(ScreenCastFrame frame) => screenCasts.Publish(UnitId, Context.ConnectionId, frame);

    public void ScreenCaptureStopped(Guid sessionId) => screenCasts.CaptureStopped(UnitId, Context.ConnectionId, sessionId);
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
        applications.Disconnected(UnitId, Context.ConnectionId);
        await screenCasts.AgentDisconnectedAsync(UnitId, Context.ConnectionId);
        presence.Disconnected(UnitId, Context.ConnectionId);
        logger.LogInformation("Agent disconnected for unit {ComputerUnitId}.",
            UnitId);
        await base.OnDisconnectedAsync(exception);
    }
}
