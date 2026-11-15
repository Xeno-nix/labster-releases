using CLMS.Agent.Models;

namespace CLMS.Agent.Services;

public interface IScreenCaptureService
{
    /// <summary>Returns an in-memory JPEG of a desktop display using the current platform's implementation.</summary>
    /// <exception cref="UnauthorizedAccessException">The operating system denied screen-recording access.</exception>
    Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>Releases any persistent native capture session when viewing ends.</summary>
    Task StopAsync() => Task.CompletedTask;
}
