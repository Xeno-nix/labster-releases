#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CLMS.Agent.Core;
using Microsoft.UI.Windowing;

namespace CLMS.Agent.Views;

internal sealed class StudentKioskPage(AgentCoordinator identity, AgentApi api) : ContentPage
{
    private readonly Entry email = new() { Placeholder = "School email", Keyboard = Keyboard.Email, MaxLength = 254 };
    private readonly Entry password = new() { Placeholder = "Password", IsPassword = true, MaxLength = 128 };
    private readonly Entry code = new() { Placeholder = "6-character code (case-sensitive)", MaxLength = 6 };
    private readonly Entry replacement = new() { Placeholder = "New password (15 or more characters)", IsPassword = true, MaxLength = 128 };
    private readonly Label status = new() { Text = "Use your student ID as the password on your first sign-in." };
    private readonly VerticalStackLayout form = new() { Spacing = 12 };
    private readonly VerticalStackLayout change = new() { Spacing = 12, IsVisible = false };
    private readonly VerticalStackLayout apps = new() { Spacing = 12, IsVisible = false };
    private readonly List<Process> launched = [];
    private AppWindow? native;
    private Guid session = Guid.NewGuid();
    private IDispatcherTimer? timer;
    private bool active, busy;

    internal void Initialize(Window window)
    {
        native = ((Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!).AppWindow;
        native.Closing += (_, e) => e.Cancel = true;
        native.SetPresenter(AppWindowPresenterKind.FullScreen);
        var root = new VerticalStackLayout { Padding = 36, Spacing = 16, MaximumWidthRequest = 500 };
        root.Children.Add(new Label { Text = "CLMS student sign-in", FontSize = 30, FontAttributes = FontAttributes.Bold });
        root.Children.Add(status);
        root.Children.Add(form);
        root.Children.Add(apps);
        form.Children.Add(email);
        form.Children.Add(password);
        form.Children.Add(Button("Sign in", SignIn));
        form.Children.Add(Button("Reset password", BeginReset));
        form.Children.Add(change);
        change.Children.Add(code);
        change.Children.Add(replacement);
        change.Children.Add(Button("Save password", SavePassword));
        apps.Children.Add(Button("End session", End));
        LoadApprovedApps();

        Content = new ScrollView { Content = root };
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(20);
        timer.Tick += async (_, _) =>
        {
            if (!active || busy) return;
            await Run(async () =>
            {
                if (Idle()) { await End(); return; }
                try { await Request<bool>("renew", new { sessionId = session }); }
                catch
                {
                    Lock();
                    status.Text = "Connection lost. Sign in again when CLMS is available.";
                }
            });
        };
        timer.Start();
    }

    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Text = label };
        button.Clicked += async (_, _) => await Run(action);
        return button;
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (AgentApiException e)
        {
            status.Text = e.Status switch
            {
                400 => "Check the email, verification code, and password requirements.",
                401 => "Sign-in failed. Check your password and current enrollment.",
                409 => "This workstation still has a session. Try again in two minutes.",
                429 => "Too many attempts. Wait and try again.",
                _ => "CLMS could not complete the request. Try again."
            };
        }
        catch { status.Text = "Unable to complete the request. Check the CLMS connection or ask staff."; }
        finally { busy = false; }
    }

    private async Task<T> Request<T>(string action, object payload)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = await identity.GetAccessTokenAsync(timeout.Token);
        var origin = identity.Status.ServerAddress;
        if (token is null || origin is null)
        {
            status.Text = "Staff must register this workstation before student sign-in.";
            throw new AgentApiException(400, "registration_required");
        }
        return await api.Kiosk<T>(origin, action, payload, token, timeout.Token);
    }

    private async Task SignIn()
    {
        var entered = password.Text ?? "";
        password.Text = "";
        var result = await Request<LoginResult>("login", new { email = email.Text?.Trim(), password = entered, sessionId = session });
        if (result.ChangeRequired)
        {
            change.IsVisible = true;
            status.Text = "Check your school email for the verification code.";
            return;
        }

        active = true;
        form.IsVisible = false;
        change.IsVisible = false;
        code.Text = replacement.Text = "";
        apps.IsVisible = true;
        // The shell remains running so End session is available when students return to it.
        native?.SetPresenter(AppWindowPresenterKind.Overlapped);
        status.Text = "Session active. Open an approved app or end your session.";
    }

    private async Task BeginReset()
    {
        await Request<bool>("reset", new { email = email.Text?.Trim() });
        change.IsVisible = true;
        status.Text = "If the account is eligible, a code will arrive at your school email.";
    }

    private async Task SavePassword()
    {
        var entered = replacement.Text ?? "";
        replacement.Text = "";
        await Request<bool>("password", new { email = email.Text?.Trim(), code = code.Text?.Trim(), password = entered });
        code.Text = "";
        change.IsVisible = false;
        status.Text = "Password saved. Sign in with your new password.";
    }

    private async Task End()
    {
        var ended = session;
        Lock();
        await Request<bool>("logout", new { sessionId = ended });
    }

    private void Lock()
    {
        active = false;
        session = Guid.NewGuid();
        foreach (var process in launched)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        launched.Clear();
        native?.SetPresenter(AppWindowPresenterKind.FullScreen);
        if (native is not null) SetForegroundWindow(native.Id);
        apps.IsVisible = false;
        form.IsVisible = true;
        change.IsVisible = false;
        email.Text = password.Text = code.Text = replacement.Text = "";
        status.Text = "Sign in to start a session.";
    }

    private void LoadApprovedApps()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "kiosk-apps.json");
            if (!File.Exists(file)) return;
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
            foreach (var (label, path) in entries)
            {
                if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                apps.Children.Add(Button(label, () =>
                {
                    if (active && Process.Start(new ProcessStartInfo(path) { UseShellExecute = false }) is { } process)
                        launched.Add(process);
                    return Task.CompletedTask;
                }));
            }
        }
        catch { apps.Children.Add(new Label { Text = "Approved app list unavailable. Contact lab staff." }); }
    }

    private static bool Idle()
    {
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.Tick) >= 900_000;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size; public uint Tick; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput input);

    private static void SetForegroundWindow(Microsoft.UI.WindowId id) =>
        BringToForeground(Microsoft.UI.Win32Interop.GetWindowFromWindowId(id));

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool BringToForeground(nint window);

    private sealed record LoginResult(bool ChangeRequired);
}
#endif
