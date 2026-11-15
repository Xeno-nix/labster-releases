namespace CLMS.Application.Interfaces.Repositories;

public interface IScreenCastRepository
{
    Task<int> CreateAsync(Guid initiatedBy, IReadOnlyCollection<Guid> units, DateTime startedAt);
    Task SetConnectionAsync(int sessionId, Guid unitId, bool connected, DateTime at);
    Task EndAsync(int sessionId, bool failed, DateTime endedAt);
    Task RecoverInterruptedAsync(DateTime endedAt);
}
