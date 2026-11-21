using CLMS.Agent.Services;
using CLMS.Shared.Dtos;
using Xunit;

namespace CLMS.Tests;

public sealed class WorkstationCommandTests
{
    [Fact]
    public async Task ConcurrentDuplicatePowerRequestsExecuteOnlyOnce()
    {
        var actions = new FakeActions();
        var handler = new WorkstationCommandHandler(actions);
        var command = Command(WorkstationCommandKind.Shutdown);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => handler.ExecuteAsync(command, default)));
        Assert.Equal(1, actions.Calls);
        Assert.All(results, result => Assert.Equal(new WorkstationCommandResult(command.Id, WorkstationCommandStatus.Requested), result));
    }

    [Fact]
    public async Task ExpiredAndUnreasonablyFutureCommandsNeverExecute()
    {
        var actions = new FakeActions();
        var handler = new WorkstationCommandHandler(actions);
        foreach (var expiry in new[] { DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1) })
        {
            var command = Command(WorkstationCommandKind.Restart) with { ExpiresAt = expiry };
            Assert.Equal(WorkstationCommandStatus.Expired, (await handler.ExecuteAsync(command, default)).Status);
        }
        Assert.Equal(0, actions.Calls);
    }

    [Theory]
    [InlineData(WorkstationCommandKind.SendMessage, null)]
    [InlineData(WorkstationCommandKind.SendMessage, " ")]
    [InlineData(WorkstationCommandKind.Lock, "unwanted payload")]
    [InlineData((WorkstationCommandKind)999, null)]
    public async Task InvalidPayloadDoesNotReachOperatingSystem(WorkstationCommandKind kind, string? message)
    {
        var actions = new FakeActions();
        var handler = new WorkstationCommandHandler(actions);
        Assert.Equal(WorkstationCommandStatus.Failed, (await handler.ExecuteAsync(Command(kind) with { Message = message }, default)).Status);
        Assert.Equal(0, actions.Calls);
    }

    [Fact]
    public async Task AgentFailuresAreReturnedWithoutLeakingNativeDetails()
    {
        var handler = new WorkstationCommandHandler(new FailingActions());
        Assert.Equal(WorkstationCommandStatus.Failed, (await handler.ExecuteAsync(Command(WorkstationCommandKind.Lock), default)).Status);
    }

    private static WorkstationCommand Command(WorkstationCommandKind kind) => new(Guid.NewGuid(), kind, null, DateTimeOffset.UtcNow.AddSeconds(20));
    private sealed class FakeActions : IWorkstationActions
    {
        public int Calls;
        public async Task<WorkstationCommandStatus> ExecuteAsync(WorkstationCommandKind kind, string? message, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            await Task.Yield();
            return WorkstationCommandStatus.Requested;
        }
    }
    private sealed class FailingActions : IWorkstationActions
    {
        public Task<WorkstationCommandStatus> ExecuteAsync(WorkstationCommandKind kind, string? message, CancellationToken token)
            => throw new InvalidOperationException("Native details");
    }
}
