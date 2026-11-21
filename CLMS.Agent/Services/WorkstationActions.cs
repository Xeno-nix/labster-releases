using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

public sealed class WorkstationActions(IDesktopNotifications notifications) : IWorkstationActions
{
    public async Task<WorkstationCommandStatus> ExecuteAsync(WorkstationCommandKind kind, string? message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkstationCommandProtocol.Validate(kind, message);
        if (kind == WorkstationCommandKind.SendMessage)
        {
            var result = await notifications.ShowAsync(new SystemNotification(Guid.NewGuid(), "Administrator message",
                message!, NotificationSeverity.Information, DateTimeOffset.UtcNow.AddSeconds(20))).WaitAsync(cancellationToken);
            return result switch
            {
                DesktopNotificationStatus.Submitted => WorkstationCommandStatus.MessageReceived,
                DesktopNotificationStatus.Disabled => WorkstationCommandStatus.NotificationsDisabled,
                DesktopNotificationStatus.Expired => WorkstationCommandStatus.Expired,
                DesktopNotificationStatus.Unsupported => WorkstationCommandStatus.Unsupported,
                _ => WorkstationCommandStatus.Failed
            };
        }

        if (OperatingSystem.IsWindows())
        {
            if (kind == WorkstationCommandKind.Lock)
            {
                if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
                return WorkstationCommandStatus.Applied;
            }
            await RunAsync(Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                [kind == WorkstationCommandKind.Restart ? "/r" : "/s", "/t", "0"], cancellationToken);
            return WorkstationCommandStatus.Requested;
        }
        if (OperatingSystem.IsMacCatalyst())
        {
            var script = kind switch
            {
                WorkstationCommandKind.Lock => "tell application \"System Events\" to keystroke \"q\" using {command down, control down}",
                WorkstationCommandKind.Restart => "tell application \"System Events\" to restart",
                WorkstationCommandKind.Shutdown => "tell application \"System Events\" to shut down",
                _ => throw new ArgumentException("Unknown workstation command.")
            };
            await RunAsync("/usr/bin/osascript", ["-e", script], cancellationToken);
            return WorkstationCommandStatus.Requested;
        }
        return WorkstationCommandStatus.Unsupported;
    }

    private static async Task RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The system action could not start.");
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("The operating system denied the action.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
