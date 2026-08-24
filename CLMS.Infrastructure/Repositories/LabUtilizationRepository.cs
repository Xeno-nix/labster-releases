using System.Collections.Concurrent;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Domain.Repositories;

namespace CLMS.Infrastructure.Repositories;

/// <summary>
/// TEMPORARY in-memory implementation — see remarks on InMemoryStudentRepository.
/// Registered as a singleton so data survives for the lifetime of the app process.
/// </summary>
public class InMemoryLabUtilizationRepository : ILabUtilizationRepository
{
    private readonly ConcurrentDictionary<int, LaboratoryUseLog> _logs = new();
    private readonly IGuestRepository _guestRepository;
    private int _nextId;

    public InMemoryLabUtilizationRepository(IGuestRepository guestRepository)
    {
        _guestRepository = guestRepository ?? throw new ArgumentNullException(nameof(guestRepository));
    }

    public Task AddAsync(LaboratoryUseLog log, CancellationToken cancellationToken = default)
    {
        if (log == null) throw new ArgumentNullException(nameof(log));

        log.Id = Interlocked.Increment(ref _nextId);
        _logs[log.Id] = log;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(LaboratoryUseLog log, CancellationToken cancellationToken = default)
    {
        if (log == null) throw new ArgumentNullException(nameof(log));

        _logs[log.Id] = log;
        return Task.CompletedTask;
    }

    public Task<LaboratoryUseLog?> GetActiveSessionForStudentAsync(string studentId, CancellationToken cancellationToken = default)
    {
        var match = _logs.Values
            .Where(l => l.StudentId == studentId && l.TimeOut is null)
            .OrderByDescending(l => l.TimeIn)
            .FirstOrDefault();

        return Task.FromResult(match);
    }

    public async Task<List<LaboratoryUseLog>> GetActiveSessionsForGuestNameAsync(string guestName, CancellationToken cancellationToken = default)
    {
        var results = new List<LaboratoryUseLog>();

        foreach (var log in _logs.Values.Where(l => l.GuestLogId is not null && l.TimeOut is null))
        {
            var guest = await _guestRepository.GetByIdAsync(log.GuestLogId!.Value, cancellationToken).ConfigureAwait(false);
            if (guest is not null && guest.GuestName == guestName)
                results.Add(log);
        }

        return results.OrderByDescending(l => l.TimeIn).ToList();
    }
}
