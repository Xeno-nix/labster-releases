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
    private readonly SettingsSession session;
    private bool authenticating, validating;
    private DateTimeOffset nextValidation;
    private bool windowActive = true;
    private IDispatcherTimer? timer;
    private bool busy, updating;
    private bool serverLoaded;
    private string? displayedServer;
    public ObservableCollection<RegistrationLaboratory> Laboratories { get; } = [];

    public MainPage(AgentCoordinator identity, AgentRuntimeService runtime, AgentConnectionService connection,
        ScreenCastSender sender, IDesktopAgentService desktop, SettingsSession session)
    {
        this.identity = identity; this.runtime = runtime; this.connection = connection;
        this.sender = sender; this.desktop = desktop;
        this.session = session;
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
        try { await identity.InitializeAsync(default); }
        catch { LoginError.Text = "Unable to load local enrollment. Check protected storage permissions."; LoginError.IsVisible = true; }
    }
    protected override void OnDisappearing()
    {
        timer?.Stop();
        // Visibility never owns the runtime, credentials, transport, or capture lifetime.
        base.OnDisappearing();
    }
    private async void OnTick(object? sender, EventArgs args)
    {
        RefreshStatus();
        if (!session.IsUnlocked || validating || busy || DateTimeOffset.UtcNow < nextValidation) return;
        validating = true;
        try { await session.RequireAccessAsync(); nextValidation = DateTimeOffset.UtcNow.AddSeconds(30); }
        catch { LockSettings(); }
        finally { validating = false; }
    }
    public void LockSettings()
    {
        session.Lock();
        PasswordEntry.Text = CodeEntry.Text = "";
        CodeLoginPanel.IsVisible = false;
        CredentialsPanel.IsVisible = true;
        LoginPanel.IsVisible = true;
        SettingsPanel.IsVisible = false;
    }
    public void SetWindowActive(bool active)
    {
        windowActive = active;
        PasswordEntry.Text = "";
        // Keep a pending email challenge while the administrator reads their email.
        if (!active && session.IsUnlocked) LockSettings();
    }
    private async void LoginClicked(object? sender, EventArgs e) => await Authenticate(async () =>
    {
        var password = PasswordEntry.Text ?? "";
        PasswordEntry.Text = "";
        await identity.InitializeAsync(default);
        var server = identity.Status.ServerAddress
            ?? Environment.GetEnvironmentVariable("CLMS_AGENT_SERVER_URL")
            ?? "https://localhost:7190";
        await session.SignInAsync(server, EmailEntry.Text ?? "", password);
    });
    private async void VerifyClicked(object? sender, EventArgs e) => await Authenticate(async () =>
    {
        var code = CodeEntry.Text ?? "";
        CodeEntry.Text = "";
        await session.VerifyAsync(code);
    });
    private async void ResendClicked(object? sender, EventArgs e) => await Authenticate(session.ResendAsync);
    private void BackClicked(object? sender, EventArgs e) => LockSettings();
    private async Task Authenticate(Func<Task> action)
    {
        if (authenticating) return;
        authenticating = true;
        LoginError.IsVisible = false;
        LoginButton.IsEnabled = VerifyButton.IsEnabled = ResendButton.IsEnabled = false;
        LoginActivity.IsVisible = LoginActivity.IsRunning = true;
        try
        {
            await action();
            if (session.IsUnlocked && !windowActive) { LockSettings(); return; }
            CodeLoginPanel.IsVisible = session.NeedsCode;
            CredentialsPanel.IsVisible = !session.NeedsCode;
            if (session.IsUnlocked)
            {
                if (!identity.Status.CanConfigure && identity.Status.ServerAddress is { } existing &&
                    AgentApi.ValidateAddress(existing) != AgentApi.ValidateAddress(session.Server!))
                    throw new UnauthorizedAccessException("Sign in to the server used by this enrolled agent.");
                if (identity.Status.CanConfigure)
                {
                    await identity.ConfigureAsync(session.Server!, default);
                    await runtime.StartAsync();
                }
                nextValidation = DateTimeOffset.UtcNow.AddSeconds(30);
                RefreshStatus();
                if (identity.Status.CanRegister && Laboratories.Count == 0) await Execute(LoadLaboratories);
            }
        }
        catch (Exception ex)
        {
            if (!session.NeedsCode) LockSettings();
            LoginError.Text = ex is UnauthorizedAccessException or ArgumentException ? ex.Message
                : "Unable to reach the server. Check the HTTPS address, certificate trust, and connection.";
            LoginError.IsVisible = true;
        }
        finally
        {
            authenticating = false;
            LoginButton.IsEnabled = VerifyButton.IsEnabled = ResendButton.IsEnabled = true;
            LoginActivity.IsVisible = LoginActivity.IsRunning = false;
        }
    }
    private void LockClicked(object? sender, EventArgs e) => LockSettings();
    private void RefreshStatus()
    {
        if (!session.IsUnlocked)
        {
            if (SettingsPanel.IsVisible) LockSettings();
            return;
        }
        LoginPanel.IsVisible = false;
        SettingsPanel.IsVisible = true;
        var status = identity.Status;
        PhaseLabel.Text = status.Phase;
        MessageLabel.Text = status.Message;
        RuntimeLabel.Text = runtime.Status;
        ConnectionLabel.Text = connection.Snapshot.Status;
        SharingLabel.Text = this.sender.Status;
        ServerPanel.IsVisible = status.CanConfigure;
        if (!serverLoaded || displayedServer != status.ServerAddress)
        {
            ServerEntry.Text = status.ServerAddress ?? "https://localhost:7190";
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
        StartupSwitch.IsEnabled = HiddenSwitch.IsEnabled = owns && !busy;
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
        try { await session.RequireAccessAsync(); await action(); }
        catch (UnauthorizedAccessException) { LockSettings(); }
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
    private async void ConfigureClicked(object? sender, EventArgs e) => await Execute(async () =>
    {
        await identity.ConfigureAsync(session.Server ?? "", default);
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
        if (!updating) SaveHiddenAsync(e.Value);
    }
    private async void SaveHiddenAsync(bool value) => await Execute(() =>
    { Preferences.Default.Set("agent.start-hidden", value); return Task.CompletedTask; });
    private void HideClicked(object? sender, EventArgs e) => desktop.Hide();
    public async Task RequestQuitAsync()
    {
        if (!session.IsUnlocked) { desktop.Show(); return; }
        await Execute(() => ((App)Microsoft.Maui.Controls.Application.Current!).QuitAgentAsync());
    }
    private async void QuitClicked(object? sender, EventArgs e) => await RequestQuitAsync();
}
