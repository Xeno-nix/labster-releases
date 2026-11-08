using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace CLMS.Agent.Core;

/// <summary>Captures an identification-only client token without doing managed identity inspection while impersonating.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsPipeClient
{
    /// <summary>Checks enabled interactive/network SIDs after restoring the service identity; never executes commands as the client.</summary>
    public static bool IsLocalInteractive(NamedPipeServerStream pipe) => Inspect(pipe, false);

    /// <summary>Requires an enabled administrator SID; a filtered non-elevated token cannot rotate keys.</summary>
    public static bool IsLocalAdministrator(NamedPipeServerStream pipe) => Inspect(pipe, true);

    private static bool Inspect(NamedPipeServerStream pipe, bool administrator)
    {
        IntPtr token = IntPtr.Zero;
        var error = 0;
        var thread = GetCurrentThread();
        // Bind the native entry points before entering the identification context.
        if (OpenThreadToken(thread, 0x0008, true, out var existing)) CloseHandle(existing);
        PipeStreamImpersonationWorker capture = () =>
        {
            if (!OpenThreadToken(thread, 0x0008, true, out token)) error = Marshal.GetLastWin32Error();
        };
        RuntimeHelpers.PrepareDelegate(capture);
        try
        {
            pipe.RunAsClient(capture);
            if (error != 0) throw new Win32Exception(error);
            if (token == IntPtr.Zero) throw new Win32Exception(1008); // ERROR_NO_TOKEN
            // Managed identity APIs can lazily load assemblies. Identification does not
            // authorize filesystem access, so use them only after RunAsClient reverts.
            using var identity = new WindowsIdentity(token);
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null)) &&
                !principal.IsInRole(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)) &&
                (!administrator || principal.IsInRole(WindowsBuiltInRole.Administrator));
        }
        finally { if (token != IntPtr.Zero) CloseHandle(token); }
    }

    /// <summary>Gets the current thread pseudo-handle for a synchronous impersonation callback.</summary>
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    /// <summary>Captures the client's thread token with query rights only; openAsSelf uses the service for the access check.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenThreadToken(IntPtr thread, uint access, bool openAsSelf, out IntPtr token);
    /// <summary>Closes an owned token handle after identity inspection.</summary>
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
