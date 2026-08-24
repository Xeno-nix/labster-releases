using System.Collections.Concurrent;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Repositories;

namespace CLMS.Infrastructure.Repositories;

/// <summary>
/// TEMPORARY in-memory implementation — see remarks on InMemoryStudentRepository.
/// Registered as a singleton so data survives for the lifetime of the app process.
///
/// Seeds two pre-existing ACTIVE sessions on startup so the "already has an active
/// session" error and the Time-Out flow can both be tested immediately, without first
/// performing a Time-In:
///   Student 2024-00456 (Maria Santos) — active since ~1 hour ago
///   Guest   "Pedro Guest"             — active since ~30 minutes ago
/// Remove this seeding (the constructor body below the field declarations) once real
/// persistence is in place.
/// </summary>
public class InMemoryLabUtilizationRepository : ILabUtilizationRepository
{
    private readonly ConcurrentDictionary<int, LaboratoryUseLog> _logs = new();
    private readonly IGuestRepository _guestRepository;
    private int _nextId;

    public InMemoryLabUtilizationRepository(IGuestRepository guestRepository)
    {
        _guestRepository = guestRepository ?? throw new ArgumentNullException(nameof(guestRepository));

        SeedTestData();
    }

    private void SeedTestData()
    {
        // Active student session — Maria Santos (2024-00456).
        var studentLog = new LaboratoryUseLog
        {
            Id = Interlocked.Increment(ref _nextId),
            StudentId = "2024-00456",
            Purpose = "Laboratory session",
            LogDate = DateOnly.FromDateTime(DateTime.Now),
            TimeIn = DateTime.Now.AddHours(-1),
            TimeOut = null,
        };
        _logs[studentLog.Id] = studentLog;

        // Active guest session — "Pedro Guest". AddAsync is async but the in-memory
        // repo completes synchronously, so blocking here during startup seeding is safe.
        var guest = new Guest("Pedro Guest", "Visitor (reservation: Sample Reservation)");
        var savedGuest = _guestRepository.AddAsync(guest).GetAwaiter().GetResult();

        var guestLog = new LaboratoryUseLog
        {
            Id = Interlocked.Increment(ref _nextId),
            GuestLogId = savedGuest.Id,
            Purpose = guest.Purpose ?? string.Empty,
            LogDate = DateOnly.FromDateTime(DateTime.Now),
            TimeIn = DateTime.Now.AddMinutes(-30),
            TimeOut = null,
        };
        _logs[guestLog.Id] = guestLog;
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

    // ── TEMP-DEBUG ─────────────────────────────────────────────────
    // Not part of ILabUtilizationRepository — only used by the temporary browser-console
    // logger (see CLMS.Web/Debug/TerminalDebugLogger.cs) to dump current state for
    // testing. Delete this method when that debug logger is removed.
    public IReadOnlyList<LaboratoryUseLog> GetAllForDebug()
        => _logs.Values.OrderBy(l => l.Id).ToList();
}
