using System.ComponentModel;
using System.Runtime.InteropServices;
using CLMS.Agent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.Win32;

namespace CLMS.Agent.Platforms.Windows;

public sealed class WindowsDesktopAgentService(ILogger<WindowsDesktopAgentService> logger) : IDesktopAgentService
{
    private readonly ILogger<WindowsDesktopAgentService> log = logger;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "CLMS.Agent";
    private const uint TrayMessage = 0x8001;
    private readonly SubclassProc callback = WindowProc;
    private static readonly Dictionary<nint, WindowsDesktopAgentService> windows = [];
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly uint showSettings = RegisterWindowMessage("CLMS.Agent.ShowSettings");
    private nint hwnd;
    private AppWindow? appWindow;
    private NotifyIconData icon;
    private Func<Task>? stopSharing;
    private Func<Task>? quit;
    private bool disposed;
    private bool iconAdded;

    public bool StartupEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string value && value == StartupCommand;
        }
    }
    private static string StartupCommand => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.")}\"";
    public string StartupStatus => StartupEnabled
        ? "Registered for this user's sign-in. Windows Startup Apps settings can override this. Re-enable after moving the app."
        : "Not registered for this user's sign-in.";

    public Task SetStartupEnabledAsync(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(RunName, StartupCommand);
        else key.DeleteValue(RunName, false);
        return Task.CompletedTask;
    }

    public void Initialize(Window window, Func<Task> stopSharing, Func<Task> quit)
    {
        this.stopSharing = stopSharing;
        this.quit = quit;
        var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        appWindow = native.AppWindow;
        windows.Add(hwnd, this);
        if (!SetWindowSubclass(hwnd, callback, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot attach tray callbacks.");
        icon = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1,
            Flags = 1 | 2 | 4, CallbackMessage = TrayMessage,
            Icon = LoadIcon(0, (nint)32512), Tip = "Labster — starting",
            Info = string.Empty, InfoTitle = string.Empty
        };
        AddIcon();
        if (!iconAdded) throw new InvalidOperationException("Cannot create the Labster tray indicator. Settings will remain visible.");
        appWindow.Closing += OnClosing;
    }

    private void AddIcon() => iconAdded = Shell_NotifyIcon(0, ref icon);

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (disposed) return;
        args.Cancel = true;
        Hide();
    }

    public void Show()
    {
        if (disposed || hwnd == 0) return;
        ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
    }

    public void Hide()
    {
        // Never hide the only settings entry point if the tray failed to initialize.
        if (!disposed && iconAdded) ShowWindow(hwnd, 0);
    }

    public void SetStatus(string status)
    {
        if (disposed || !iconAdded) return;
        var tooltip = "Labster — " + status;
        icon.Tip = tooltip[..Math.Min(127, tooltip.Length)];
        Shell_NotifyIcon(1, ref icon);
    }

    private static nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (windows.TryGetValue(hwnd, out var service) && !service.disposed)
        {
            try
            {
                if (message == service.taskbarCreated) service.AddIcon();
                if (message == service.showSettings) service.Show();
                if (message == TrayMessage)
                {
                    var mouse = (uint)lParam;
                    if (mouse == 0x0202 || mouse == 0x0203) service.Show();
                    else if (mouse == 0x0205 || mouse == 0x007B) service.OpenMenu();
                    return 0;
                }
            }
            catch (Exception ex) { service.log.LogWarning(ex, "Tray action failed."); }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void OpenMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            AppendMenu(menu, 0, 1, "Open settings");
            AppendMenu(menu, 0, 2, "Stop screen sharing");
            AppendMenu(menu, 0, 3, "Quit agent");
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var selected = TrackPopupMenu(menu, 0x0100 | 0x0002, point.X, point.Y, 0, hwnd, 0);
            PostMessage(hwnd, 0, 0, 0);
            if (selected == 1) Show();
            else if (selected == 2) RunAction(stopSharing);
            else if (selected == 3) RunAction(quit);
        }
        finally { DestroyMenu(menu); }
    }

    private void RunAction(Func<Task>? action) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        try { if (action is not null) await action(); }
        catch (Exception ex) { log.LogWarning(ex, "Agent menu action failed."); }
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (appWindow is not null) appWindow.Closing -= OnClosing;
        if (iconAdded) Shell_NotifyIcon(2, ref icon);
        if (hwnd != 0)
        {
            RemoveWindowSubclass(hwnd, callback, 1);
            windows.Remove(hwnd);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
