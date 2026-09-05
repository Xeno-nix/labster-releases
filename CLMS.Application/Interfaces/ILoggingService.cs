using CLMS.Domain.Entities.UtilizationLogging;

namespace CLMS.Application.Interfaces;

/// <summary>
/// Core laboratory-logging business rules. Mirrors Laravel's App\Services\LoggingService.
/// Lives in the Application layer (not Domain) so it can depend on the booking module's
/// IClassScheduleRepository/IBookingRepository, matching this project's established
/// convention of keeping repository interfaces and orchestration logic in Application
/// rather than Domain (see IBookingService/IClassScheduleService).
/// </summary>
public interface ILoggingService
{
    /// <summary>
    /// Time-in for a registered student. Resolves lab/purpose/instructor from the
    /// student's currently-active class schedule. Throws DomainException when the
    /// student is not found, already has an active session, or has no class in session
    /// right now.
    /// </summary>
    Task<LaboratoryUseLog> StudentTimeInAsync(string studentId, DateTime timestamp);

    /// <summary>
    /// Unified time-out for both students and visitors, keyed off a single identifier
    /// (student number or full name). Throws DomainException when no matching active
    /// session exists, or when multiple active guest sessions share the same name.
    /// </summary>
    Task<LaboratoryUseLog> TimeOutAsync(string identifier, DateTime timestamp);

    /// <summary>
    /// Time-in for a visitor. Resolves purpose/lab from a booking made under
    /// bookedUnder that is currently in its reserved time window. Throws
    /// DomainException when the visitor already has an active session or no matching
    /// booking is found right now. Note: the current `bookings` schema has no
    /// approval-status column, so any booking found for the right name/date/time is
    /// treated as valid — there is no pending/approved/rejected distinction available.
    /// </summary>
    Task<LaboratoryUseLog> GuestTimeInAsync(string guestName, string bookedUnder, DateTime timestamp);
}
