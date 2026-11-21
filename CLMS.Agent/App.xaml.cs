using CLMS.Agent.Services;
using CLMS.Agent.Views;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly MainPage settings;
    private readonly AgentRuntimeService runtime;
    private readonly AgentConnectionService connection;
    private readonly ScreenCastSender screenCast;
    private readonly IDesktopAgentService desktop;
    private readonly ILogger<App> logger;
    private bool quitting;
    private FileStream? processLock;
    public bool OwnsLifetime { get; private set; }
    public string? StartupError { get; private set; }

    public App(MainPage settings, AgentRuntimeService runtime, AgentConnectionService connection,
        ScreenCastSender screenCast, IDesktopAgentService desktop, ILogger<App> logger)
    {
        InitializeComponent();
        this.settings = settings;
        this.runtime = runtime;
        this.connection = connection;
        this.screenCast = screenCast;
        this.desktop = desktop;
        this.logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(settings) { Title = "Labster settings", Width = 660, Height = 800 };
        window.Created += async (_, _) =>
        {
            try
            {
                desktop.Initialize(window, () => screenCast.StopAsync(), QuitAgentAsync);
                var directory = CLMS.Agent.Service.UserAgentVault.StateDirectory;
                Directory.CreateDirectory(directory);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                processLock = new FileStream(Path.Combine(directory, "desktop.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                OwnsLifetime = true;
                runtime.StatusChanged += UpdateIndicator;
                connection.StatusChanged += OnConnectionChanged;
                screenCast.StatusChanged += UpdateIndicator;
                var configured = await runtime.StartAsync();
                if (configured && Preferences.Default.Get("agent.start-hidden", true))
                    MainThread.BeginInvokeOnMainThread(desktop.Hide);
            }
            catch (Exception ex)
            {
                StartupError = "Agent startup failed. Close other instances, check user storage permissions, and reopen the app.";
                logger.LogError(ex, "Agent startup failed; keeping settings visible.");
                desktop.Show();
            }
        };
        window.Destroying += async (_, _) => await QuitAgentAsync();
        return window;
    }

    private void OnConnectionChanged(Models.AgentConnectionSnapshot _) => UpdateIndicator(connection.Snapshot.Status);

    private void UpdateIndicator(string _) => MainThread.BeginInvokeOnMainThread(() =>
        desktop.SetStatus(screenCast.IsSharing ? "Sharing screen with an authorized viewer"
            : connection.IsRunning ? connection.Snapshot.Status : runtime.Status));

    public async Task QuitAgentAsync()
    {
        if (quitting) return;
        quitting = true;
        try { await runtime.StopAsync(); }
        catch (Exception ex) { logger.LogWarning(ex, "Agent shutdown cleanup failed."); }
        finally
        {
            runtime.StatusChanged -= UpdateIndicator;
            connection.StatusChanged -= OnConnectionChanged;
            screenCast.StatusChanged -= UpdateIndicator;
            desktop.Dispose();
            OwnsLifetime = false;
            processLock?.Dispose();
            Quit();
        }
    }
}
