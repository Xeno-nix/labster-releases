using CLMS.Agent.Models;
using CLMS.Agent.Services;

namespace CLMS.Agent.Views;

public partial class MainPage : ContentPage
{
    private readonly AgentConnectionService connection;
    private readonly ScreenCastSender screenCast;
    private readonly AgentSetupStore setup;
    private readonly AgentRuntimeService runtime;
    private readonly IDesktopAgentService desktop;
    private bool updatingControls = true;
    private bool subscribed;

    public MainPage(AgentConnectionService connection, ScreenCastSender screenCast,
        AgentSetupStore setup, AgentRuntimeService runtime, IDesktopAgentService desktop)
    {
        InitializeComponent();
        this.connection = connection;
        this.screenCast = screenCast;
        this.setup = setup;
        this.runtime = runtime;
        this.desktop = desktop;
        ServerAddressEntry.Text = setup.ServerAddress;
        StartHiddenSwitch.IsToggled = Preferences.Default.Get("agent.start-hidden", true);
    }

    private void SetBusy(bool busy)
    {
        SaveSetupButton.IsEnabled = !busy;
        ConnectButton.IsEnabled = !busy;
        DisconnectButton.IsEnabled = !busy;
        ServerAddressEntry.IsEnabled = !busy;
        DeviceKeyEntry.IsEnabled = !busy;
    }

    private async void OnSaveSetupClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            var key = string.IsNullOrWhiteSpace(DeviceKeyEntry.Text)
                ? await setup.GetDeviceKeyAsync() : DeviceKeyEntry.Text;
            await setup.SaveAsync(ServerAddressEntry.Text ?? "", key ?? "");
            ServerAddressEntry.Text = setup.ServerAddress;
            DeviceKeyEntry.Text = string.Empty;
            DeviceKeyEntry.Placeholder = "Saved device key (enter a new key to replace)";
            SetupStatusLabel.Text = "Setup saved for this OS user.";
            await runtime.StartAsync();
        }
        catch (ArgumentException ex) { SetupStatusLabel.Text = ex.Message; }
        catch (Exception) { SetupStatusLabel.Text = "Could not save setup. Check secure storage access and try again."; }
        finally { SetBusy(false); }
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            await runtime.StartAsync();
            SetupStatusLabel.Text = "Using saved setup. Save any edited address or key before reconnecting.";
        }
        catch (Exception) { SetupStatusLabel.Text = "Could not restart the agent. Try again or quit and reopen it."; }
        finally { SetBusy(false); }
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        try { await runtime.StopAsync(); }
        catch (Exception) { SetupStatusLabel.Text = "Could not pause the connection. Quit the agent to stop it."; }
        finally { SetBusy(false); }
    }

    private async void OnStopSharingClicked(object? sender, EventArgs e)
    {
        try { await screenCast.StopAsync(); }
        catch (Exception) { SharingStatusLabel.Text = "Could not stop screen sharing. Pause the connection or quit the agent."; }
    }

    private void OnSharingStatusChanged(string status) => MainThread.BeginInvokeOnMainThread(() =>
    {
        SharingStatusLabel.Text = status;
        StopSharingButton.IsEnabled = screenCast.IsSharing;
    });

    private void OnConnectionStatusChanged(AgentConnectionSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() =>
        ConnectionStatusLabel.Text = snapshot.ComputerUnitId.HasValue
            ? $"{snapshot.Status} · Unit {snapshot.ComputerUnitId} · Acknowledged {snapshot.LastAcknowledgedUtc?.ToLocalTime():HH:mm:ss}"
            : snapshot.Status);

    private void OnRuntimeStatusChanged(string status) => MainThread.BeginInvokeOnMainThread(() => RuntimeStatusLabel.Text = status);

    private void RefreshStartup()
    {
        updatingControls = true;
        try
        {
            StartupSwitch.IsToggled = desktop.StartupEnabled;
            StartupStatusLabel.Text = desktop.StartupStatus;
        }
        finally { updatingControls = false; }
    }

    private async void OnStartupToggled(object? sender, ToggledEventArgs e)
    {
        if (updatingControls) return;
        StartupSwitch.IsEnabled = false;
        try
        {
            await desktop.SetStartupEnabledAsync(e.Value);
            RefreshStartup();
        }
        catch (Exception)
        {
            RefreshStartup();
            StartupStatusLabel.Text = "Could not change login startup. Check OS permissions and that the app is installed in a permanent location.";
        }
        finally { StartupSwitch.IsEnabled = true; }
    }

    private void OnStartHiddenToggled(object? sender, ToggledEventArgs e)
    {
        if (!updatingControls) Preferences.Default.Set("agent.start-hidden", e.Value);
    }

    private void OnHideClicked(object? sender, EventArgs e) => desktop.Hide();
    private async void OnQuitClicked(object? sender, EventArgs e) => await ((App)Microsoft.Maui.Controls.Application.Current!).QuitAgentAsync();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!subscribed)
        {
            subscribed = true;
            connection.StatusChanged += OnConnectionStatusChanged;
            screenCast.StatusChanged += OnSharingStatusChanged;
            runtime.StatusChanged += OnRuntimeStatusChanged;
        }
        OnSharingStatusChanged(screenCast.Status);
        OnConnectionStatusChanged(connection.Snapshot);
        OnRuntimeStatusChanged(runtime.Status);
        RefreshStartup();
        SetBusy(true);
        try
        {
            var hasKey = !string.IsNullOrWhiteSpace(await setup.GetDeviceKeyAsync());
            DeviceKeyEntry.Placeholder = hasKey ? "Saved device key (enter a new key to replace)" : "Device key for this unit";
            SetupStatusLabel.Text = hasKey ? "Saved setup loaded." : "Enter the server address and device key, then save setup.";
        }
        catch (Exception) { SetupStatusLabel.Text = "Cannot read the saved key. Enter it again and save setup."; }
        finally { SetBusy(false); }
    }

    protected override void OnDisappearing()
    {
        if (subscribed)
        {
            connection.StatusChanged -= OnConnectionStatusChanged;
            screenCast.StatusChanged -= OnSharingStatusChanged;
            runtime.StatusChanged -= OnRuntimeStatusChanged;
            subscribed = false;
        }
        // A settings window does not own the connection or screen sender.
        base.OnDisappearing();
    }
}
