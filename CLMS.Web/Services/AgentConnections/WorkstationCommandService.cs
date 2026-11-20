using CLMS.Application.Identity.Interfaces;
using CLMS.Application.UnitRegistry;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.AgentConnections;

public sealed class WorkstationCommandService(ICurrentUser actor, IUnitRegistryRepository registry,
    AgentPresenceStore presence, IHubContext<AgentConnectionHub> hub, TimeProvider clock,
    ILogger<WorkstationCommandService> logger)
{
    public async Task<WorkstationCommandStatus> ExecuteAsync(Guid unitId, WorkstationCommandKind kind, string? message = null,
        CancellationToken cancellationToken = default)
    {
        var adminId = await actor.RequireAdminAsync();
        cancellationToken.ThrowIfCancellationRequested();
        message = message?.Trim();
        WorkstationCommandProtocol.Validate(kind, message);
        var unit = presence.Snapshot().GetValueOrDefault(unitId);
        if (unit is null || !unit.Connected)
            throw new InvalidOperationException("This workstation is offline.");
        var credential = presence.Credential(unitId, unit.ConnectionId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        if (credential is null || !await registry.IsAuthorizedAsync(unitId, credential.Generation, credential.CredentialId, timeout.Token))
            throw new InvalidOperationException("This workstation is no longer authorized.");
        if (presence.Credential(unitId, unit.ConnectionId) != credential)
            throw new InvalidOperationException("The workstation connection changed. Try again.");
        var command = new WorkstationCommand(Guid.NewGuid(), kind, message, clock.GetUtcNow().AddSeconds(20));
        logger.LogInformation("Administrator {AdminId} requested {CommandKind} on unit {UnitId}.", adminId, kind, unitId);
        var result = await hub.Clients.Client(unit.ConnectionId)
            .InvokeAsync<WorkstationCommandResult>(WorkstationCommandProtocol.Execute, command, timeout.Token);
        if (result is null || result.Id != command.Id || !Enum.IsDefined(result.Status))
            throw new InvalidOperationException("The workstation returned an invalid response.");
        logger.LogInformation("Workstation command {CommandId} returned {Status}.", command.Id, result.Status);
        return result.Status;
    }
}
