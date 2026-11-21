using System.Collections.ObjectModel;
using CLMS.Agent.Core;
using CLMS.Agent.Services;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Views;

public partial class MainPage : ContentPage
{
    private readonly AgentCoordinator identity;
    private readonly AgentRuntimeService runtime;
    private readonly AgentConnectionService connection;
    private readonly ScreenCastSender sender;
    private readonly IDesktopAgentService desktop;
    private readonly AccessMonitoringService monitoring;
    private readonly IDesktopNotifications notifications;
    private IDispatcherTimer? timer;
    private bool busy, updating;
    private bool serverLoaded;
    private string? displayedServer;
    public ObservableCollection<RegistrationLaboratory> Laboratories { get; } = [];

    public MainPage(AgentCoordinator identity, AgentRuntimeService runtime, AgentConnectionService connection,
        ScreenCastSender sender, IDesktopAgentService desktop, AccessMonitoringService monitoring, IDesktopNotifications notifications)
    {
        this.identity = identity; this.runtime = runtime; this.connection = connection;
        this.sender = sender; this.desktop = desktop; this.monitoring = monitoring;
        this.notifications = notifications;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        timer ??= Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick -= OnTick;
        timer.Tick += OnTick;
        timer.Start();
        RefreshStatus();
        if (identity.Status.CanRegister && Laboratories.Count == 0) await Execute(LoadLaboratories);
    }
    protected override void OnDisappearing()
    {
        timer?.Stop();
        // Visibility never owns the runtime, credentials, transport, or capture lifetime.
        base.OnDisappearing();
    }
    private void OnTick(object? sender, EventArgs args) => RefreshStatus();
    private void RefreshStatus()
    {
        var status = identity.Status;
        PhaseLabel.Text = status.Phase;
        MessageLabel.Text = status.Message;
        RuntimeLabel.Text = runtime.Status;
        ConnectionLabel.Text = connection.Snapshot.Status;
        SharingLabel.Text = this.sender.Status;
        MonitoringLabel.Text = monitoring.Status;
        NotificationsLabel.Text = notifications.Status;
        NotificationsButton.Text = notifications.Enabled ? "Disable desktop notifications" : "Enable desktop notifications";
        ServerPanel.IsVisible = status.CanConfigure;
        if (!serverLoaded || displayedServer != status.ServerAddress)
        {
            ServerEntry.Text = status.ServerAddress ?? "https://localhost:4312";
            displayedServer = status.ServerAddress;
            serverLoaded = true;
        }
        if (Microsoft.Maui.Controls.Application.Current is App { StartupError: { } startupError })
        {
            ErrorLabel.Text = startupError; ErrorLabel.IsVisible = true;
        }
        RegistrationPanel.IsVisible = status.CanRegister;
        CodePanel.IsVisible = status.RegistrationCode is not null;
        CodeLabel.Text = status.RegistrationCode;
        ExpiryLabel.Text = status.ExpiresAtUtc is { } expires ? $"Expires {expires.ToLocalTime():g}" : "";
        UnitLabel.IsVisible = status.ComputerUnitId.HasValue;
        UnitLabel.Text = $"Registered unit: {status.ComputerUnitId}";
        RetryButton.IsVisible = status.CanRetry;
        updating = true;
        try
        {
            StartupSwitch.IsToggled = desktop.StartupEnabled;
            StartupLabel.Text = desktop.StartupStatus;
            HiddenSwitch.IsToggled = Preferences.Default.Get("agent.start-hidden", true);
        }
        catch { StartupLabel.Text = "Login startup status is unavailable."; }
        finally { updating = false; }
        UpdateControls();
    }
    private void UpdateControls()
    {
        if (RegisterButton is null) return;
        var owns = Microsoft.Maui.Controls.Application.Current is App app && app.OwnsLifetime;
        RegisterButton.IsEnabled = owns && !busy && LaboratoryPicker.SelectedItem is RegistrationLaboratory;
        ConfigureButton.IsEnabled = LaboratoriesButton.IsEnabled = RetryButton.IsEnabled = owns && !busy;
        RefreshStatusButton.IsEnabled = ConnectButton.IsEnabled = PauseButton.IsEnabled = owns && !busy;
        RotateButton.IsEnabled = owns && !busy && identity.Status.Phase == "Connected";
        ResetButton.IsEnabled = owns && !busy;
        StopSharingButton.IsEnabled = sender.IsSharing;
        Activity.IsVisible = Activity.IsRunning = busy;
    }
    private async Task Execute(Func<Task> action)
    {
        if (busy) return;
        if (Microsoft.Maui.Controls.Application.Current is not App app || !app.OwnsLifetime)
        {
            ErrorLabel.Text = "Agent state is unavailable or another instance owns it. Close other instances, check user storage access, and reopen this app.";
            ErrorLabel.IsVisible = true;
            return;
        }
        busy = true; ErrorLabel.IsVisible = false; UpdateControls();
        try { await action(); }
        catch (ArgumentException ex) { ErrorLabel.Text = ex.Message; ErrorLabel.IsVisible = true; }
        catch
        {
            ErrorLabel.Text = "The operation failed. Check protected storage, server availability, certificate trust, and enrollment status.";
            ErrorLabel.IsVisible = true;
        }
        finally { busy = false; RefreshStatus(); }
    }
    private async Task LoadLaboratories()
    {
        var labs = await identity.LaboratoriesAsync(CancellationToken.None);
        Laboratories.Clear();
        foreach (var lab in labs) Laboratories.Add(lab);
        LaboratoriesMessageLabel.Text = "No laboratories are available. Ask an administrator to configure one.";
        LaboratoriesMessageLabel.IsVisible = labs.Count == 0;
    }
    private void LaboratoryChanged(object? sender, EventArgs e) => UpdateControls();
    private async void NotificationsClicked(object? sender, EventArgs e) => await Execute(async () =>
        await notifications.SetEnabledAsync(!notifications.Enabled));
    private async void ConfigureClicked(object? sender, EventArgs e) => await Execute(async () =>
    {
        await identity.ConfigureAsync(ServerEntry.Text ?? "", default);
        Laboratories.Clear();
        await runtime.StartAsync();
        await LoadLaboratories();
    });
    private async void LaboratoriesClicked(object? sender, EventArgs e) => await Execute(LoadLaboratories);
    private async void RegisterClicked(object? sender, EventArgs e) => await Execute(async () =>
    {
        if (LaboratoryPicker.SelectedItem is not RegistrationLaboratory lab || !int.TryParse(UnitNumberEntry.Text, out var unit) || unit is < 1 or > 255)
            throw new ArgumentException("Select a laboratory and a unit number from 1 to 255.");
        await identity.RegisterAsync(lab.Id, unit, AppInfo.Current.VersionString, default);
        await runtime.StartAsync();
    });
    private async void RetryClicked(object? sender, EventArgs e) => await Execute(async () =>
    {
        await identity.RetryAsync(default); await runtime.StartAsync(); await LoadLaboratories();
    });
    private void RefreshClicked(object? sender, EventArgs e) => RefreshStatus();
    private async void ConnectClicked(object? sender, EventArgs e) => await Execute(async () => { await runtime.StartAsync(); });
    private async void PauseClicked(object? sender, EventArgs e) => await Execute(runtime.StopAsync);
    private async void StopSharingClicked(object? sender, EventArgs e) => await Execute(() => this.sender.StopAsync());
    private async void RotateClicked(object? sender, EventArgs e) => await Execute(async () =>
    {
        await runtime.StopAsync();
        try { await identity.RotateKeyAsync(default); }
        finally { await runtime.StartAsync(); }
    });
    private async void ResetClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Reset local enrollment?", "This stops sharing and removes this user's local enrollment. It does not revoke the server credential. Ask an administrator to revoke it before re-enrolling.", "Reset", "Cancel")) return;
        await Execute(async () =>
        {
            await runtime.StopAsync();
            try { await identity.ResetAsync(default); Laboratories.Clear(); }
            finally { await runtime.StartAsync(); }
        });
    }
    private async void StartupChanged(object? sender, ToggledEventArgs e)
    {
        if (!updating) await Execute(() => desktop.SetStartupEnabledAsync(e.Value));
    }
    private void HiddenChanged(object? sender, ToggledEventArgs e)
    {
        if (!updating) Preferences.Default.Set("agent.start-hidden", e.Value);
    }
    private void HideClicked(object? sender, EventArgs e) => desktop.Hide();
    private async void QuitClicked(object? sender, EventArgs e) => await ((App)Microsoft.Maui.Controls.Application.Current!).QuitAgentAsync();
}
