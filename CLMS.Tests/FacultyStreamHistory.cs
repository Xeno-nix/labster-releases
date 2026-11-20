using CLMS.Application.Interfaces.Repositories;

namespace CLMS.Tests;

internal sealed class FacultyStreamHistory : IScreenCastRepository
{
    private int nextId;
    public Task<int> CreateAsync(Guid owner, IReadOnlyCollection<Guid> units, DateTime at) =>
        Task.FromResult(Interlocked.Increment(ref nextId));
    public Task SetConnectionAsync(int sessionId, Guid unitId, bool connected, DateTime at) => Task.CompletedTask;
    public Task EndAsync(int sessionId, bool failed, DateTime at) => Task.CompletedTask;
    public Task RecoverInterruptedAsync(DateTime at) => Task.CompletedTask;
}
