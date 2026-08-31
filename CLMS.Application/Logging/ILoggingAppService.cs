using CLMS.Application.Logging.Dtos;

namespace CLMS.Application.Logging;

/// <summary>
/// Application-layer use cases for the public logging terminal.
/// Maps 1:1 to LogbookController's studentTimeIn / guestTimeIn / timeOut actions.
/// </summary>
public interface ILoggingAppService
{
    Task<LoggingResult> StudentTimeInAsync(StudentTimeInRequest request, CancellationToken cancellationToken = default);
    Task<LoggingResult> GuestTimeInAsync(GuestTimeInRequest request, CancellationToken cancellationToken = default);
    Task<LoggingResult> TimeOutAsync(TimeOutRequest request, CancellationToken cancellationToken = default);
}
