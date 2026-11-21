using CLMS.Agent.Services;
using CLMS.Agent.Views;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider services;
    private MainPage? settings;
    private readonly AgentRuntimeService runtime;
    private readonly AgentConnectionService connection;
    private readonly ScreenCastSender screenCast;
    private readonly IDesktopAgentService desktop;
    private readonly ILogger<App> logger;
<<<<<<< HEAD
=======
    private readonly AccessMonitoringService monitoring;
    private readonly IDesktopNotifications notifications;
    private readonly Core.AgentCoordinator identity;
    private readonly Core.AgentApi api;
>>>>>>> c350177 (Fix kiosk startup and use six-character verification codes)
    private bool quitting;
    private FileStream? processLock;
    public bool OwnsLifetime { get; private set; }
    public string? StartupError { get; private set; }

<<<<<<< HEAD
    public App(IServiceProvider services, AgentRuntimeService runtime, AgentConnectionService connection,
        ScreenCastSender screenCast, IDesktopAgentService desktop, ILogger<App> logger)
=======
    public App(MainPage settings, AgentRuntimeService runtime, AgentConnectionService connection,
        ScreenCastSender screenCast, IDesktopAgentService desktop, ILogger<App> logger, AccessMonitoringService monitoring,
        IDesktopNotifications notifications, Core.AgentCoordinator identity, Core.AgentApi api)
>>>>>>> c350177 (Fix kiosk startup and use six-character verification codes)
    {
        InitializeComponent();
        this.services = services;
        this.runtime = runtime;
        this.connection = connection;
        this.screenCast = screenCast;
        this.desktop = desktop;
        this.logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Page XAML uses application theme resources. Resolve it only after
        // InitializeComponent has loaded App.xaml, rather than during App constructor injection.
        var settings = services.GetRequiredService<MainPage>();
        this.settings = settings;
#if WINDOWS
        var kiosk = Environment.GetCommandLineArgs().Contains("--kiosk", StringComparer.Ordinal)
            ? new StudentKioskPage(services.GetRequiredService<Core.AgentCoordinator>(), services.GetRequiredService<Core.AgentApi>()) : null;
        Page page = (Page?)kiosk ?? settings;
#else
        Page page = settings;
#endif
        var window = new Window(page) { Title = "Labster | Agent settings", Width = 820, Height = 860, MinimumWidth = 560, MinimumHeight = 640 };
        window.Created += async (_, _) =>
        {
            try
            {
#if WINDOWS
                if (kiosk is not null) kiosk.Initialize(window);
                else
#endif
                    desktop.Initialize(window, () => screenCast.StopAsync(), settings.RequestQuitAsync);
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
#if WINDOWS
                if (kiosk is not null) return;
#endif
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
        window.Deactivated += (_, _) => settings.SetWindowActive(false);
        window.Activated += (_, _) => settings.SetWindowActive(true);
        return window;
    }

    private void OnConnectionChanged(Models.AgentConnectionSnapshot _) => UpdateIndicator(connection.Snapshot.Status);

    private void UpdateIndicator(string _) => MainThread.BeginInvokeOnMainThread(() =>
        desktop.SetStatus(screenCast.IsSharing ? "Sharing screen with an authorized viewer"
            : connection.IsRunning ? connection.Snapshot.Status : runtime.Status));

    public void LockSettings() => settings?.LockSettings();

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
