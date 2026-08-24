using CLMS.Domain.Entities.UtilizationLogging;

namespace CLMS.Domain.Services;

/// <summary>
/// Core laboratory-logging business rules. Mirrors Laravel's App\Services\LoggingService.
/// NOTE: Student/Guest time-in are currently placeholder implementations pending the
/// booking module's ClassSchedule/Booking work — see LoggingService's class remarks.
/// </summary>
public interface ILoggingService
{
    /// <summary>
    /// Time-in for a registered student. Throws DomainException when the student is not
    /// found or already has an active session.
    /// TODO(booking-integration): once wired up, this should also resolve lab/purpose/
    /// instructor from the student's currently-active class schedule and throw when none
    /// is in session (see LoggingService remarks).
    /// </summary>
    Task<LaboratoryUseLog> StudentTimeInAsync(string studentId, DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unified time-out for both students and visitors, keyed off a single identifier
    /// (student number or full name). Throws DomainException when no matching active
    /// session exists, or when multiple active guest sessions share the same name.
    /// Fully implemented; does not depend on booking data.
    /// </summary>
    Task<LaboratoryUseLog> TimeOutAsync(string identifier, DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Time-in for a visitor. Throws DomainException when the visitor already has an
    /// active session.
    /// TODO(booking-integration): once wired up, this should validate bookedUnder against
    /// an approved Booking in its reserved time window and resolve lab/purpose from it
    /// (see LoggingService remarks).
    /// </summary>
    Task<LaboratoryUseLog> GuestTimeInAsync(string guestName, string bookedUnder, DateTime timestamp, CancellationToken cancellationToken = default);
}
