using CLMS.Application.Logging.Dtos;

namespace CLMS.Application.Logging;

public interface ILoggingAppService
{
    Task<LoggingResult> StudentTimeInAsync(StudentTimeInRequest request, CancellationToken cancellationToken = default);
    Task<LoggingResult> GuestTimeInAsync(GuestTimeInRequest request, CancellationToken cancellationToken = default);
    Task<LoggingResult> TimeOutAsync(TimeOutRequest request, CancellationToken cancellationToken = default);
}
