using System.Collections.Concurrent;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Repositories;

namespace CLMS.Infrastructure.Repositories;

/// <summary>
/// TEMPORARY in-memory implementation — see remarks on InMemoryStudentRepository.
/// Registered as a singleton so data survives for the lifetime of the app process.
/// </summary>
public class InMemoryGuestRepository : IGuestRepository
{
    private readonly ConcurrentDictionary<int, Guest> _guests = new();
    private int _nextId;

    public Task<Guest> AddAsync(Guest guest, CancellationToken cancellationToken = default)
    {
        if (guest == null) throw new ArgumentNullException(nameof(guest));

        var id = Interlocked.Increment(ref _nextId);
        guest.AssignId(id);
        _guests[id] = guest;
        return Task.FromResult(guest);
    }

    public Task<Guest?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(_guests.TryGetValue(id, out var guest) ? guest : null);
}
