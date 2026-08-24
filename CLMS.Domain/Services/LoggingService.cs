using CLMS.Domain.Common;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Domain.Repositories;

namespace CLMS.Domain.Services;

/// <summary>
/// Port of Laravel's App\Services\LoggingService, scoped to what's currently resolvable
/// in this project (i.e. without depending on the booking module's ClassSchedule/Booking
/// data, which is still under active development by another dev).
///
/// TODO(booking-integration): Laravel resolves the laboratory/instructor/purpose for a
/// student time-in from their currently-active ClassSchedule, and validates a guest
/// time-in against an approved Booking. Neither is wired up here yet:
///   - Student: once ClassSchedule exposes a laboratory + a way to find "the student's
///     class in session right now" (e.g. via Student.GetCurrentClassSchedule), replace
///     the placeholder purpose/lab/instructor below with real values from that lookup,
///     and mirror Laravel's "No class is scheduled for you..." error when none is found.
///   - Guest: once Booking has a status (pending/approved/rejected) and the fields
///     needed to match "booked under" + time window, replace the placeholder purpose/lab
///     below with a real lookup via a booking repository, and mirror Laravel's booking
///     validation messages (pending / outside window / not found).
/// The unified TimeOutAsync below does not depend on booking data at all and is fully
/// implemented to match Laravel's behavior.
/// </summary>
public class LoggingService : ILoggingService
{
    private readonly ILabUtilizationRepository _labUtilizationRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly IGuestRepository _guestRepository;

    public LoggingService(
        ILabUtilizationRepository labUtilizationRepository,
        IStudentRepository studentRepository,
        IGuestRepository guestRepository)
    {
        _labUtilizationRepository = labUtilizationRepository ?? throw new ArgumentNullException(nameof(labUtilizationRepository));
        _studentRepository = studentRepository ?? throw new ArgumentNullException(nameof(studentRepository));
        _guestRepository = guestRepository ?? throw new ArgumentNullException(nameof(guestRepository));
    }

    // ── Student Time In ─────────────────────────────────────────────
    // Laravel: LoggingService::studentTimeIn($studentId)
    public async Task<LaboratoryUseLog> StudentTimeInAsync(string studentId, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(studentId, cancellationToken).ConfigureAwait(false);
        if (student is null)
            throw new DomainException("Student not found.");

        var existingSession = await _labUtilizationRepository
            .GetActiveSessionForStudentAsync(student.StudentId, cancellationToken)
            .ConfigureAwait(false);

        if (existingSession is not null)
            throw new DomainException("You already have an active session. Please sign out first.");

        // TODO(booking-integration): replace this placeholder with a real lookup of the
        // student's currently-active ClassSchedule (course/lab/instructor/day/time window),
        // and throw DomainException("No class is scheduled for you in any laboratory at
        // this time.") when none is found — see class-level remarks.
        var log = new LaboratoryUseLog
        {
            StudentId = student.StudentId,
            Purpose = "Laboratory session",
            LaboratoryId = null,
            InstructorId = null,
            ClassScheduleId = null,
            LogDate = DateOnly.FromDateTime(timestamp),
            TimeIn = timestamp,
        };

        await _labUtilizationRepository.AddAsync(log, cancellationToken).ConfigureAwait(false);
        return log;
    }

    // ── Unified Time Out ────────────────────────────────────────────
    // Laravel: LoggingService::timeOut($identifier)
    // Tries the identifier as a student number first; if no student matches,
    // treats it as a visitor name and looks up an active guest session.
    // Fully implemented — does not depend on booking data.
    public async Task<LaboratoryUseLog> TimeOutAsync(string identifier, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(identifier, cancellationToken).ConfigureAwait(false);
        if (student is not null)
        {
            var activeStudentLog = await _labUtilizationRepository
                .GetActiveSessionForStudentAsync(student.StudentId, cancellationToken)
                .ConfigureAwait(false);

            if (activeStudentLog is null)
                throw new DomainException("No active session found for this student.");

            activeStudentLog.TimeOut = timestamp;
            await _labUtilizationRepository.UpdateAsync(activeStudentLog, cancellationToken).ConfigureAwait(false);
            return activeStudentLog;
        }

        var guestLogs = await _labUtilizationRepository
            .GetActiveSessionsForGuestNameAsync(identifier, cancellationToken)
            .ConfigureAwait(false);

        if (guestLogs.Count == 0)
            throw new DomainException("No active session found with that student number or name.");

        if (guestLogs.Count > 1)
            throw new DomainException("Multiple active visitors share that name. Please contact laboratory staff.");

        var guestLog = guestLogs[0];
        guestLog.TimeOut = timestamp;
        await _labUtilizationRepository.UpdateAsync(guestLog, cancellationToken).ConfigureAwait(false);
        return guestLog;
    }

    // ── Guest Time In ───────────────────────────────────────────────
    // Laravel: LoggingService::guestTimeIn($guestName, $bookedUnder)
    public async Task<LaboratoryUseLog> GuestTimeInAsync(string guestName, string bookedUnder, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var activeSessions = await _labUtilizationRepository
            .GetActiveSessionsForGuestNameAsync(guestName, cancellationToken)
            .ConfigureAwait(false);

        if (activeSessions.Count > 0)
            throw new DomainException("You already have an active session. Please sign out first.");

        // TODO(booking-integration): replace this placeholder with a real lookup of an
        // approved Booking under `bookedUnder` that is in its reserved time window right
        // now, and mirror Laravel's explainMissingBooking() messages (pending / outside
        // window / not found / multiple matches) — see class-level remarks. The guestName
        // vs bookedUnder distinction (individual attendee vs the reservation's name) is
        // preserved so the terminal UI/DTOs don't need to change once this is wired up.
        var guest = new Entities.Users.Guest(guestName, $"Visitor (reservation: {bookedUnder})");
        var savedGuest = await _guestRepository.AddAsync(guest, cancellationToken).ConfigureAwait(false);

        var log = new LaboratoryUseLog
        {
            GuestLogId = savedGuest.Id,
            Purpose = guest.Purpose ?? string.Empty,
            LaboratoryId = null,
            ClassScheduleId = null,
            LogDate = DateOnly.FromDateTime(timestamp),
            TimeIn = timestamp,
        };

        await _labUtilizationRepository.AddAsync(log, cancellationToken).ConfigureAwait(false);
        return log;
    }
}
