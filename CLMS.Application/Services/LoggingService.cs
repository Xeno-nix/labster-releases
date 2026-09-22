using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Common;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Domain.Entities.Users;

namespace CLMS.Application.Services;

/// <summary>
/// Port of Laravel's App\Services\LoggingService, integrated against the real booking
/// module (IClassScheduleRepository for student time-in, IBookingRepository for guest
/// time-in). Business rules and messages are kept as close to the Laravel source as the
/// current `bookings` schema allows — see remarks on ILoggingService.GuestTimeInAsync
/// re: the missing approval-status column.
/// </summary>
public class LoggingService : ILoggingService
{
    private readonly ILabUtilizationRepository _labUtilizationRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly IGuestRepository _guestRepository;
    private readonly IClassScheduleRepository _classScheduleRepository;
    private readonly IBookingRepository _bookingRepository;

    public LoggingService(
        ILabUtilizationRepository labUtilizationRepository,
        IStudentRepository studentRepository,
        IGuestRepository guestRepository,
        IClassScheduleRepository classScheduleRepository,
        IBookingRepository bookingRepository)
    {
        _labUtilizationRepository = labUtilizationRepository ?? throw new ArgumentNullException(nameof(labUtilizationRepository));
        _studentRepository = studentRepository ?? throw new ArgumentNullException(nameof(studentRepository));
        _guestRepository = guestRepository ?? throw new ArgumentNullException(nameof(guestRepository));
        _classScheduleRepository = classScheduleRepository ?? throw new ArgumentNullException(nameof(classScheduleRepository));
        _bookingRepository = bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
    }

    // ── Student Time In ─────────────────────────────────────────────
    // Laravel: LoggingService::studentTimeIn($studentId)
    public async Task<LaboratoryUseLog> StudentTimeInAsync(string studentId, DateTime timestamp)
    {
        var student = await _studentRepository.GetByIdAsync(studentId).ConfigureAwait(false);
        if (student is null)
            throw new DomainException("Student not found.");

        var existingSession = await _labUtilizationRepository
            .GetActiveSessionForStudentAsync(student.StudentId)
            .ConfigureAwait(false);

        if (existingSession is not null)
            throw new DomainException("You already have an active session. Please sign out first.");

        var day = timestamp.DayOfWeek;
        var currentTime = TimeOnly.FromDateTime(timestamp);

        var schedule = await _classScheduleRepository
            .FindCurrentForStudentAsync(student.StudentId, day, currentTime)
            .ConfigureAwait(false);

        if (schedule is null)
            throw new DomainException("No class is scheduled for you in any laboratory at this time.");

        var purpose = $"{schedule.CourseCode} - {schedule.CourseName}".Trim();

        var log = new LaboratoryUseLog
        {
            StudentId = student.StudentId,
            Purpose = purpose,
            LaboratoryId = schedule.LaboratoryId,
            InstructorId = schedule.InstructorId,
            LogDate = DateOnly.FromDateTime(timestamp),
            TimeIn = timestamp,
        };

        await _labUtilizationRepository.CreateAsync(log).ConfigureAwait(false);
        return log;
    }

    // ── Unified Time Out ────────────────────────────────────────────
    // Laravel: LoggingService::timeOut($identifier)
    // Tries the identifier as a student number first; if no student matches,
    // treats it as a visitor name and looks up an active guest session.
    public async Task<LaboratoryUseLog> TimeOutAsync(string identifier, DateTime timestamp)
    {
        var student = await _studentRepository.GetByIdAsync(identifier).ConfigureAwait(false);
        if (student is not null)
        {
            var activeStudentLog = await _labUtilizationRepository
                .GetActiveSessionForStudentAsync(student.StudentId)
                .ConfigureAwait(false);

            if (activeStudentLog is null)
                throw new DomainException("No active session found for this student.");

            activeStudentLog.TimeOut = timestamp;
            await _labUtilizationRepository.UpdateAsync(activeStudentLog).ConfigureAwait(false);
            return activeStudentLog;
        }

        var guestLogs = await _labUtilizationRepository
            .GetActiveSessionsForGuestNameAsync(identifier)
            .ConfigureAwait(false);

        if (guestLogs.Count == 0)
            throw new DomainException("No active session found with that student number or name.");

        if (guestLogs.Count > 1)
            throw new DomainException("Multiple active visitors share that name. Please contact laboratory staff.");

        var guestLog = guestLogs[0];
        guestLog.TimeOut = timestamp;
        await _labUtilizationRepository.UpdateAsync(guestLog).ConfigureAwait(false);
        return guestLog;
    }

    // ── Guest Time In ───────────────────────────────────────────────
    // Laravel: LoggingService::guestTimeIn($guestName, $bookedUnder)
    public async Task<LaboratoryUseLog> GuestTimeInAsync(string guestName, string bookedUnder, DateTime timestamp)
    {
        var activeSessions = await _labUtilizationRepository
            .GetActiveSessionsForGuestNameAsync(guestName)
            .ConfigureAwait(false);

        if (activeSessions.Count > 0)
            throw new DomainException("You already have an active session. Please sign out first.");

        var today = DateOnly.FromDateTime(timestamp);
        var currentTime = TimeOnly.FromDateTime(timestamp);

        var todaysBookings = (await _bookingRepository
            .FindByRequestorNameAndDateAsync(bookedUnder, today)
            .ConfigureAwait(false))
            .ToList();

        Console.WriteLine($"Found {todaysBookings.Count} bookings for '{bookedUnder}' on {today:yyyy-MM-dd}.");
        var matches = todaysBookings
            .Where(b => b.StartTime <= currentTime && b.EndTime >= currentTime)
            .ToList();

        if (matches.Count == 0)
        {
            // No booking-status column exists in the current schema (see class
            // remarks), so we can only distinguish "found but wrong time" vs
            // "nothing found today" — there's no "still pending" case to detect.
            var nextToday = todaysBookings.OrderBy(b => b.StartTime).FirstOrDefault();
            if (nextToday is not null)
            {
                var time = nextToday.StartTime.ToString("h:mm tt");
                throw new DomainException($"Your reservation is scheduled for {time}. Please come back during your scheduled time.");
            }

            throw new DomainException("No reservation found for that name today. Please book a reservation first.");
        }

        if (matches.Count > 1)
            throw new DomainException("Multiple reservations match that booker name right now. Please contact laboratory staff.");

        var booking = matches[0];

        var guest = new Guest(guestName, booking.Purpose);
        await _guestRepository.CreateAsync(guest).ConfigureAwait(false);

        var log = new LaboratoryUseLog
        {
            GuestId = guest.Id,
            Purpose = booking.Purpose,
            LaboratoryId = booking.LaboratoryId,
            LogDate = today,
            TimeIn = timestamp,
        };

        await _labUtilizationRepository.CreateAsync(log).ConfigureAwait(false);
        return log;
    }
}
