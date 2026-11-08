using CLMS.Agent.Core;
using CLMS.Shared.Dtos;
using System.Collections.ObjectModel;

namespace CLMS.Agent.Views;

public partial class MainPage : ContentPage
{
    private CancellationTokenSource? lifetime;
    private bool busy, laboratoriesLoaded, loadingLaboratories;
    private string? laboratoryServerAddress;
    private int laboratoryLoadFailures;
    private DateTimeOffset nextLaboratoryLoad;
    public ObservableCollection<RegistrationLaboratory> Laboratories { get; } = [];
    /// <summary>Initializes the registration and service-status controls.</summary>
    public MainPage()
    {
        InitializeComponent();
        UpdateControls();
    }
    /// <summary>Starts a fresh page lifetime, loads service status, and begins polling while the page is visible.</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = new();
        await Refresh(lifetime.Token);
        _ = Poll(lifetime.Token);
    }
    /// <summary>Cancels page polling without stopping the independent machine service.</summary>
    protected override void OnDisappearing() { lifetime?.Cancel(); base.OnDisappearing(); }
    /// <summary>Refreshes status periodically while the page is visible and no operator action is in progress.</summary>
    private async Task Poll(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try { while (await timer.WaitForNextTickAsync(ct)) if (!busy) await Refresh(ct); }
        catch (OperationCanceledException) { }
    }
    /// <summary>Requests local service status and loads laboratory choices when registration is available. Converts connection and identity-verification failures into the service-unavailable display.</summary>
    private async Task Refresh(CancellationToken ct)
    {
        try
        {
            var response = await AgentIpc.RequestAsync(new("status"), ct);
            Apply(response);
            if (response.Error is null && response.Status is { CanRegister: true, Phase: "RegistrationRequired" } &&
                !laboratoriesLoaded && DateTimeOffset.UtcNow >= nextLaboratoryLoad) await LoadLaboratories(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (AgentIpcException ex)
        {
            PhaseLabel.Text = "Agent service verification failed";
            MessageLabel.Text = ex.Message;
            ShowError(ex.Code + (ex.NativeError.HasValue ? $" (Windows error {ex.NativeError})" : ""));
            ServerPanel.IsVisible = RegistrationPanel.IsVisible = CodePanel.IsVisible = false;
        }
        catch
        {
            PhaseLabel.Text = "Agent service unavailable";
            MessageLabel.Text = "Install or start the CLMS machine service, then refresh. Your service credentials have not been changed.";
            ServerPanel.IsVisible = RegistrationPanel.IsVisible = CodePanel.IsVisible = false;
        }
    }
    /// <summary>Updates visible controls from the service's credential-free status or displays its sanitized error.</summary>
    private void Apply(IpcResponse response)
    {
        if (response.Error is not null)
        {
            PhaseLabel.Text = "Agent service error";
            MessageLabel.Text = response.Error;
            ShowError(response.ErrorCode ?? "ipc_request_failed");
            ServerPanel.IsVisible = RegistrationPanel.IsVisible = CodePanel.IsVisible = false;
            return;
        }
        var status = response.Status;
        if (status is null) return;
        if (!string.Equals(laboratoryServerAddress, status.ServerAddress, StringComparison.Ordinal))
        {
            laboratoryServerAddress = status.ServerAddress;
            ClearLaboratories();
        }
        ErrorLabel.IsVisible = false;
        PhaseLabel.Text = status.Phase;
        MessageLabel.Text = status.Message;
        ServerPanel.IsVisible = status.CanConfigure;
        if (!ServerEntry.IsFocused) ServerEntry.Text = status.ServerAddress ?? ServerEntry.Text;
        RegistrationPanel.IsVisible = status.CanRegister;
        CodePanel.IsVisible = status.RegistrationCode is not null;
        CodeLabel.Text = status.RegistrationCode;
        ExpiryLabel.Text = status.ExpiresAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        RetryButton.IsVisible = status.CanRetry;
        UnitLabel.IsVisible = status.ComputerUnitId.HasValue;
        UnitLabel.Text = status.ComputerUnitId.HasValue ? $"Registered unit: {status.ComputerUnitId}" : "";
    }
    /// <summary>Displays a local operation error without exposing credential material.</summary>
    private void ShowError(string message) { ErrorLabel.Text = message; ErrorLabel.IsVisible = true; }
    /// <summary>Serializes UI actions, manages busy indicators, and restores controls after success, cancellation, or failure.</summary>
    private async Task Execute(Func<CancellationToken, Task> action)
    {
        if (busy || loadingLaboratories) return;
        busy = true;
        UpdateControls();
        ErrorLabel.IsVisible = false;
        try { await action(lifetime?.Token ?? CancellationToken.None); }
        catch (OperationCanceledException) { }
        catch { ShowError("The local service could not complete the request. Check service installation, network connectivity, and certificate trust."); }
        finally
        {
            busy = false;
            UpdateControls();
        }
    }
    /// <summary>Retrieves laboratory choices through the service and populates the picker after a successful response.</summary>
    private async Task LoadLaboratories(CancellationToken ct)
    {
        if (loadingLaboratories) return;
        loadingLaboratories = true;
        LaboratoriesMessageLabel.Text = "Loading laboratories…";
        LaboratoriesMessageLabel.IsVisible = true;
        UpdateControls();
        try
        {
            // The service fetches GET /api/agent/laboratories using its configured HTTPS origin.
            var response = await AgentIpc.RequestAsync(new("laboratories"), ct);
            ct.ThrowIfCancellationRequested();
            if (response.Error is not null || response.Laboratories is null)
            {
                LaboratoryLoadFailed(response.Error ?? "The service did not return laboratory options.");
                return;
            }
            var selectedId = string.Equals(laboratoryServerAddress, response.Status?.ServerAddress, StringComparison.Ordinal)
                ? (LaboratoryPicker.SelectedItem as RegistrationLaboratory)?.Id : null;
            Apply(response);
            Laboratories.Clear();
            foreach (var laboratory in response.Laboratories) Laboratories.Add(laboratory);
            LaboratoryPicker.SelectedItem = Laboratories.FirstOrDefault(lab => lab.Id == selectedId);
            laboratoriesLoaded = true;
            laboratoryLoadFailures = 0;
            nextLaboratoryLoad = DateTimeOffset.MinValue;
            LaboratoryPicker.Title = Laboratories.Count == 0 ? "No laboratories available" : "Select a laboratory";
            LaboratoriesMessageLabel.Text = "No laboratories are available. Ask an administrator to configure a laboratory, then reload.";
            LaboratoriesMessageLabel.IsVisible = Laboratories.Count == 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            LaboratoryLoadFailed("Could not retrieve laboratories. Check the service, server connection, and certificate trust.");
        }
        finally
        {
            loadingLaboratories = false;
            UpdateControls();
        }
    }
    /// <summary>Keeps the registration form usable after a failed fetch and bounds automatic retries.</summary>
    private void LaboratoryLoadFailed(string message)
    {
        laboratoriesLoaded = false;
        nextLaboratoryLoad = DateTimeOffset.UtcNow + AgentCoordinator.ReconnectDelay(laboratoryLoadFailures++);
        ShowError(message);
        LaboratoriesMessageLabel.Text = "Could not reload laboratories. Try Reload laboratories to retry now.";
        LaboratoriesMessageLabel.IsVisible = true;
    }
    /// <summary>Removes choices from a previous configuration before fetching a fresh server list.</summary>
    private void ClearLaboratories()
    {
        laboratoriesLoaded = false;
        laboratoryLoadFailures = 0;
        nextLaboratoryLoad = DateTimeOffset.MinValue;
        Laboratories.Clear();
        LaboratoryPicker.SelectedIndex = -1;
        LaboratoryPicker.Title = "Select a laboratory";
        LaboratoriesMessageLabel.IsVisible = false;
        UpdateControls();
    }
    /// <summary>Updates loading indicators and prevents registration without a selected server-provided laboratory.</summary>
    private void UpdateControls()
    {
        // Picker events can fire while InitializeComponent is still creating later controls.
        if (Activity is null || RefreshStatusButton is null) return;
        var idle = !busy && !loadingLaboratories;
        Activity.IsVisible = Activity.IsRunning = !idle;
        ConfigureButton.IsEnabled = RetryButton.IsEnabled = LaboratoriesButton.IsEnabled = RefreshStatusButton.IsEnabled = idle;
        LaboratoryPicker.IsEnabled = idle && Laboratories.Count > 0;
        UnitNumberEntry.IsEnabled = idle;
        RegisterButton.IsEnabled = idle && LaboratoryPicker.SelectedItem is RegistrationLaboratory;
    }
    /// <summary>Enables registration once the operator selects a laboratory from the server list.</summary>
    private void LaboratoryChanged(object? sender, EventArgs e) => UpdateControls();
    /// <summary>Saves the entered server origin through the service and reloads laboratory choices.</summary>
    private async void ConfigureClicked(object? sender, EventArgs e) => await Execute(async ct =>
    {
        ClearLaboratories();
        var response = await AgentIpc.RequestAsync(new("configure", ServerEntry.Text), ct);
        Apply(response);
        if (response.Error is null) await LoadLaboratories(ct);
    });
    /// <summary>Reloads laboratory choices as an explicit operator action.</summary>
    private async void LaboratoriesClicked(object? sender, EventArgs e) => await Execute(LoadLaboratories);
    /// <summary>Validates the selected assignment and asks the service to submit its device registration.</summary>
    private async void RegisterClicked(object? sender, EventArgs e) => await Execute(async ct =>
    {
        if (LaboratoryPicker.SelectedItem is not RegistrationLaboratory lab || !int.TryParse(UnitNumberEntry.Text, out var number) || number is < 1 or > 255)
        { ShowError("Select a laboratory and enter a unit number from 1 to 255."); return; }
        Apply(await AgentIpc.RequestAsync(new("register", LaboratoryId: lab.Id, UnitNumber: number), ct));
    });
    /// <summary>Asks the service to reset a terminal registration and makes laboratory choices eligible for reloading.</summary>
    private async void RetryClicked(object? sender, EventArgs e) => await Execute(async ct =>
    {
        var response = await AgentIpc.RequestAsync(new("retry"), ct);
        Apply(response);
        if (response.Error is null)
        {
            ClearLaboratories();
            await LoadLaboratories(ct);
        }
    });
    /// <summary>Runs an explicit service-status refresh with the standard UI busy-state handling.</summary>
    private async void RefreshClicked(object? sender, EventArgs e) => await Execute(Refresh);
}
