namespace CLMS.Application.Logging.Dtos;

/// <summary>
/// Mirrors the success/error flash message pair the Laravel controller returns via
/// redirect()->with('success', ...) / ->with('error', ...).
/// </summary>
public class LoggingResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static LoggingResult Succeeded(string message) => new() { Success = true, Message = message };
    public static LoggingResult Failed(string message) => new() { Success = false, Message = message };
}
