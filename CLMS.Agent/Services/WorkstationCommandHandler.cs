using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

/// <summary>Serializes actions and prevents duplicate command IDs from executing twice.</summary>
public sealed class WorkstationCommandHandler(IWorkstationActions actions, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, WorkstationCommandResult> completed = [];
    private readonly Queue<Guid> order = [];
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<WorkstationCommandResult> ExecuteAsync(WorkstationCommand command, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (completed.TryGetValue(command.Id, out var previous)) return previous;
            WorkstationCommandStatus status;
            try
            {
                WorkstationCommandProtocol.Validate(command.Kind, command.Message);
                if (command.Id == Guid.Empty || command.ExpiresAt <= time.GetUtcNow()
                    || command.ExpiresAt > time.GetUtcNow().AddSeconds(30))
                    status = WorkstationCommandStatus.Expired;
                else
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(command.ExpiresAt - time.GetUtcNow());
                    status = await actions.ExecuteAsync(command.Kind, command.Message, deadline.Token);
                }
            }
            catch (OperationCanceledException) { status = WorkstationCommandStatus.Expired; }
            catch { status = WorkstationCommandStatus.Failed; }
            var result = new WorkstationCommandResult(command.Id, status);
            completed.Add(command.Id, result);
            order.Enqueue(command.Id);
            if (order.Count > 256) completed.Remove(order.Dequeue());
            return result;
        }
        finally { gate.Release(); }
    }
}
