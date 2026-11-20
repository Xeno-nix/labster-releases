using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

public interface IWorkstationActions
{
    Task<WorkstationCommandStatus> ExecuteAsync(WorkstationCommandKind kind, string? message, CancellationToken cancellationToken);
}
