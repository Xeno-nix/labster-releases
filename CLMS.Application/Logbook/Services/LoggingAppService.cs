using System.ComponentModel.DataAnnotations;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Logging.Dtos;
using CLMS.Application.Services;
using CLMS.Domain.Common;

namespace CLMS.Application.Logging;

public class LoggingAppService
{
    private readonly LoggingService _loggingService;
    private readonly IStudentRepository _studentRepository;

    public LoggingAppService(LoggingService loggingService, IStudentRepository studentRepository)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _studentRepository = studentRepository ?? throw new ArgumentNullException(nameof(studentRepository));
    }
    
    public async Task<LoggingResult> StudentTimeInAsync(StudentTimeInRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var validationError))
            return LoggingResult.Failed(validationError);
        
        var student = await _studentRepository.GetByIdAsync(request.StudentId).ConfigureAwait(false);
        if (student is null)
            return LoggingResult.Failed("No student found with that number.");

        try
        {
            await _loggingService.StudentTimeInAsync(request.StudentId, DateTime.Now).ConfigureAwait(false);
            return LoggingResult.Succeeded("Student successfully logged in.");
        }
        catch (DomainException ex)
        {
            return LoggingResult.Failed(ex.Message);
        }
    }
    
    public async Task<LoggingResult> GuestTimeInAsync(GuestTimeInRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var validationError))
            return LoggingResult.Failed(validationError);

        try
        {
            await _loggingService.GuestTimeInAsync(request.GuestName, request.BookedUnder, DateTime.Now).ConfigureAwait(false);
            return LoggingResult.Succeeded("Welcome! You are now signed in.");
        }
        catch (DomainException ex)
        {
            return LoggingResult.Failed(ex.Message);
        }
    }
    
    public async Task<LoggingResult> TimeOutAsync(TimeOutRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var validationError))
            return LoggingResult.Failed(validationError);

        try
        {
            await _loggingService.TimeOutAsync(request.Identifier, DateTime.Now).ConfigureAwait(false);
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
