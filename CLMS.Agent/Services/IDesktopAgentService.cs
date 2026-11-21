namespace CLMS.Agent.Services;

/// <summary>Native window, visible background indicator, and per-user login registration.</summary>
public interface IDesktopAgentService : IDisposable
{
    bool StartupEnabled { get; }
    string StartupStatus { get; }
    void Initialize(Window window, Func<Task> stopSharing, Func<Task> quit);
    void Show();
    void Hide();
    void SetStatus(string status);
    Task SetStartupEnabledAsync(bool enabled);
}
