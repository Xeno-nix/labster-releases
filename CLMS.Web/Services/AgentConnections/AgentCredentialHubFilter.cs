using System.Globalization;
using CLMS.Application.UnitRegistry;
using CLMS.Web.Services.ScreenCasting;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.AgentConnections;

/// <summary>SignalR caches principals; check the live credential on every device operation.</summary>
public sealed class AgentCredentialHubFilter(IUnitRegistryRepository registry, ScreenCastService screens,
    AgentPresenceStore presence) : IHubFilter
{
    private async Task ValidateAsync(HubCallerContext context)
    {
        var user = context.User;
        var unit = Guid.TryParse(user?.FindFirst("computer_unit_id")?.Value, out var id) ? id : Guid.Empty;
        var credential = Guid.TryParse(user?.FindFirst("device_credential_id")?.Value, out var key) ? key : Guid.Empty;
        var valid = false;
        try
        {
            valid = context.GetHttpContext()?.Request.IsHttps == true
                && user?.FindFirst("principal_type")?.Value == "agent"
                && unit != Guid.Empty && credential != Guid.Empty
                && int.TryParse(user.FindFirst("credential_generation")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
                && await registry.IsAuthorizedAsync(unit, generation, credential, context.ConnectionAborted);
        }
        catch { /* Database failure must not authorize a cached credential. */ }
        if (valid) return;
        await screens.AgentDisconnectedAsync(unit, context.ConnectionId);
        presence.Disconnected(unit, context.ConnectionId);
        context.Abort();
        throw new HubException("Device authorization is unavailable or revoked.");
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await ValidateAsync(context.Context);
        return await next(context);
    }
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await ValidateAsync(context.Context);
        await next(context);
        var user = context.Context.User!;
        presence.AssociateCredential(Guid.Parse(user.FindFirst("computer_unit_id")!.Value), context.Context.ConnectionId,
            int.Parse(user.FindFirst("credential_generation")!.Value, CultureInfo.InvariantCulture),
            Guid.Parse(user.FindFirst("device_credential_id")!.Value));
    }
}
