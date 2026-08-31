using System.ComponentModel.DataAnnotations;
using CLMS.Application.Logging.Dtos;
using CLMS.Domain.Common;
using CLMS.Domain.Repositories;
using CLMS.Domain.Services;

namespace CLMS.Application.Logging;

public class LoggingAppService : ILoggingAppService
{
    private readonly ILoggingService _loggingService;
    private readonly IStudentRepository _studentRepository;

    public LoggingAppService(ILoggingService loggingService, IStudentRepository studentRepository)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _studentRepository = studentRepository ?? throw new ArgumentNullException(nameof(studentRepository));
    }

    // Laravel: StudentTimeInRequest (required + exists:students,student_id) -> LogbookController::studentTimeIn
    public async Task<LoggingResult> StudentTimeInAsync(StudentTimeInRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var validationError))
            return LoggingResult.Failed(validationError);

        // Laravel's FormRequest 'exists:students,student_id' rule runs before the controller,
        // so this is what a user actually sees for an unknown student number.
        var student = await _studentRepository.GetByIdAsync(request.StudentId, cancellationToken).ConfigureAwait(false);
        if (student is null)
            return LoggingResult.Failed("No student found with that number.");

        try
        {
            await _loggingService.StudentTimeInAsync(request.StudentId, DateTime.Now, cancellationToken).ConfigureAwait(false);
            return LoggingResult.Succeeded("Student successfully logged in.");
        }
        catch (DomainException ex)
        {
            return LoggingResult.Failed(ex.Message);
        }
    }

    // Laravel: GuestTimeInRequest -> LogbookController::guestTimeIn
    public async Task<LoggingResult> GuestTimeInAsync(GuestTimeInRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var validationError))
            return LoggingResult.Failed(validationError);

        try
        {
            await _loggingService.GuestTimeInAsync(request.GuestName, request.BookedUnder, DateTime.Now, cancellationToken).ConfigureAwait(false);
            return LoggingResult.Succeeded("Welcome! You are now signed in.");
        }
        catch (DomainException ex)
        {
            return LoggingResult.Failed(ex.Message);
        }
    }

    // Laravel: TimeOutRequest -> LogbookController::timeOut
    public async Task<LoggingResult> TimeOutAsync(TimeOutRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var validationError))
            return LoggingResult.Failed(validationError);

        try
        {
            await _loggingService.TimeOutAsync(request.Identifier, DateTime.Now, cancellationToken).ConfigureAwait(false);
            return LoggingResult.Succeeded("Successfully signed out. Have a great day!");
        }
        catch (DomainException ex)
        {
            return LoggingResult.Failed(ex.Message);
        }
    }

    private static bool TryValidate(object request, out string errorMessage)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = results.FirstOrDefault()?.ErrorMessage ?? "Invalid request.";
        return false;
    }
}
