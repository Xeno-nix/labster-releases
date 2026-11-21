using System.Runtime.InteropServices;
using CLMS.Agent.Services;
using Foundation;
using Microsoft.Extensions.Logging;
using ObjCRuntime;
using ServiceManagement;

namespace CLMS.Agent.Platforms.MacCatalyst;

/// <summary>AppKit status items use a small Objective-C bridge because Catalyst does not bind NSStatusBar.</summary>
public sealed class MacDesktopAgentService(ILogger<MacDesktopAgentService> logger) : IDesktopAgentService
{
    private nint statusBar;
    private nint statusItem;
    private nint statusText;
    private MenuActions? actions;
    private NSObject? backgroundActivity;
    private bool disposed;

    public bool StartupEnabled => (long)SMAppService.MainApp.Status == 1;
    public string StartupStatus => (long)SMAppService.MainApp.Status switch
    {
        1 => "Enabled for this user's login.",
        2 => "Approval required in System Settings > General > Login Items.",
        3 => "Login registration could not find the app. Install it in Applications and enable startup again.",
        _ => "Not registered for this user's login. Install the signed app in Applications before enabling."
    };

    public Task SetStartupEnabledAsync(bool enabled)
    {
        var service = SMAppService.MainApp;
        if (!enabled && (long)service.Status == 0) return Task.CompletedTask;
        var success = enabled ? service.Register(out var error) : service.Unregister(out error);
        if (!success) throw new InvalidOperationException(error?.LocalizedDescription ?? "Cannot change login registration.");
        return Task.CompletedTask;
    }

    public void Initialize(Window window, Func<Task> stopSharing, Func<Task> quit)
    {
        statusBar = Send(Class.GetHandle("NSStatusBar"), Sel("systemStatusBar"));
        if (statusBar == 0) throw new InvalidOperationException("The Mac menu bar is unavailable.");
        statusItem = SendDouble(statusBar, Sel("statusItemWithLength:"), -1);
        if (statusItem == 0) throw new InvalidOperationException("Cannot create the CLMS menu-bar indicator.");
        Send(statusItem, Sel("retain"));
        actions = new MenuActions(this, stopSharing, quit);
        var button = Send(statusItem, Sel("button"));
        SetText(button, "setTitle:", "CLMS");
        var menu = Send(Send(Class.GetHandle("NSMenu"), Sel("alloc")), Sel("init"));
        SendBool(menu, Sel("setAutoenablesItems:"), false);
        statusText = AddItem(menu, "CLMS Agent — starting", null);
        SendBool(statusText, Sel("setEnabled:"), false);
        AddItem(menu, "Open settings", "openSettings:");
        AddItem(menu, "Stop screen sharing", "stopSharing:");
        AddItem(menu, "Hide settings", "hideSettings:");
        AddItem(menu, "Quit agent", "quitAgent:");
        SendArg(statusItem, Sel("setMenu:"), menu);
        Send(menu, Sel("release"));
        backgroundActivity = NSProcessInfo.ProcessInfo.BeginActivity(
            NSActivityOptions.InitiatedAllowingIdleSystemSleep, "Maintain the configured CLMS agent connection");

        // Keep this single settings scene alive; users use Hide or the menu-bar actions.
        // Replacing Catalyst's NSWindow delegate would break its scene lifecycle.
        foreach (var nativeWindow in NativeWindows())
        {
            var close = SendIndex(nativeWindow, Sel("standardWindowButton:"), 0);
            if (close != 0) SendBool(close, Sel("setEnabled:"), false);
        }
    }

    private nint AddItem(nint menu, string title, string? selector)
    {
        var item = Send(Send(Class.GetHandle("NSMenuItem"), Sel("alloc")), Sel("init"));
        SetText(item, "setTitle:", title);
        if (selector is not null)
        {
            SendArg(item, Sel("setTarget:"), actions!.Handle);
            SendArg(item, Sel("setAction:"), Sel(selector));
        }
        SendArg(menu, Sel("addItem:"), item);
        Send(item, Sel("release"));
        return item; // The menu owns the item.
    }

    private static nint NativeApp => Send(Class.GetHandle("NSApplication"), Sel("sharedApplication"));
    private static IEnumerable<nint> NativeWindows()
    {
        var windows = Send(NativeApp, Sel("windows"));
        var count = (nuint)Send(windows, Sel("count"));
        for (nuint index = 0; index < count; index++)
            yield return SendIndex(windows, Sel("objectAtIndex:"), index);
    }

    public void Show()
    {
        if (disposed) return;
        SendArg(NativeApp, Sel("unhide:"), 0);
        SendBool(NativeApp, Sel("activateIgnoringOtherApps:"), true);
        foreach (var window in NativeWindows()) SendArg(window, Sel("makeKeyAndOrderFront:"), 0);
    }

    public void Hide()
    {
        if (!disposed && statusItem != 0) SendArg(NativeApp, Sel("hide:"), 0);
    }

    public void SetStatus(string status)
    {
        if (disposed || statusItem == 0) return;
        SetText(statusText, "setTitle:", "CLMS Agent — " + status);
        SetText(Send(statusItem, Sel("button")), "setTitle:", status.StartsWith("Sharing", StringComparison.Ordinal) ? "CLMS • Sharing" : "CLMS");
    }

    internal void RunAction(Func<Task> action) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        try { await action(); }
        catch (Exception ex) { logger.LogWarning(ex, "Mac menu action failed."); }
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (statusItem != 0)
        {
            SendArg(statusBar, Sel("removeStatusItem:"), statusItem);
            Send(statusItem, Sel("release"));
            statusItem = 0;
        }
        actions?.Dispose();
        if (backgroundActivity is not null)
        {
            NSProcessInfo.ProcessInfo.EndActivity(backgroundActivity);
            backgroundActivity.Dispose();
            backgroundActivity = null;
        }
    }

    private static nint Sel(string name) => Selector.GetHandle(name);
    private static void SetText(nint target, string selector, string value)
    {
        using var text = new NSString(value);
        SendArg(target, Sel(selector), text.Handle);
    }
    private const string ObjC = "/usr/lib/libobjc.dylib";
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendDouble(nint receiver, nint selector, double value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendIndex(nint receiver, nint selector, nuint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendArg(nint receiver, nint selector, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);
}

[Register("CLMSAgentMenuActions")]
public sealed class MenuActions(MacDesktopAgentService desktop, Func<Task> stopSharing, Func<Task> quit) : NSObject
{
    [Export("openSettings:")] public void OpenSettings(NSObject sender) => desktop.Show();
    [Export("hideSettings:")] public void HideSettings(NSObject sender) => desktop.Hide();
    [Export("stopSharing:")] public void StopSharing(NSObject sender) => desktop.RunAction(stopSharing);
    [Export("quitAgent:")] public void QuitAgent(NSObject sender) => desktop.RunAction(quit);
}
