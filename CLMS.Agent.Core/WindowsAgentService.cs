using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CLMS.Agent.Core;

/// <summary>Verifies the named-pipe server using read-only SCM access instead of opening a SYSTEM process.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAgentService
{
    internal const string ServiceName = "CLMS.Agent";
    internal sealed record Identity(uint ProcessId, uint State, uint Type, string BinaryPath, string Account);

    /// <summary>Requires the pipe owner to be the running, installed LocalSystem agent service.</summary>
    internal static void Verify(uint pipeProcessId)
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CLMS", "Service", "CLMS.Agent.Service.exe");
        Validate(ReadIdentity(), pipeProcessId, expected);
    }

    /// <summary>Rejects mismatched PIDs, shared-process services, accounts, and executable paths without trusting pipe contents.</summary>
    internal static void Validate(Identity service, uint pipeProcessId, string expectedPath)
    {
        if (service.State != 4 || service.ProcessId == 0)
            throw new AgentIpcException("service_not_running", "The CLMS agent service is not running. Start it and refresh.");
        if (pipeProcessId == 0 || service.ProcessId != pipeProcessId || service.Type != 0x10)
            throw new AgentIpcException("service_identity_mismatch", "The local pipe is not owned by the registered CLMS agent service.");
        if (!string.Equals(service.Account, "LocalSystem", StringComparison.OrdinalIgnoreCase))
            throw new AgentIpcException("service_account_mismatch", "The CLMS agent service must be installed under LocalSystem.");
        // Require the entire configured command to name the expected executable, with optional surrounding quotes.
        // Do not accept an executable prefix followed by arguments or an alternate launcher.
        var command = service.BinaryPath.Trim();
        if (!string.Equals(command, expectedPath, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command, '"' + expectedPath + '"', StringComparison.OrdinalIgnoreCase))
            throw new AgentIpcException("service_path_mismatch", "The registered CLMS agent executable differs from the expected installation. Repair the service installation.");
    }

    /// <summary>Reads the registered PID and configuration with SERVICE_QUERY_STATUS and SERVICE_QUERY_CONFIG rights only.</summary>
    internal static Identity ReadIdentity()
    {
        using var manager = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
        if (manager.IsInvalid) throw QueryError();
        using var service = OpenService(manager, ServiceName, 0x0001 | 0x0004);
        if (service.IsInvalid) throw QueryError();
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _)) throw QueryError();
        QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        var error = Marshal.GetLastWin32Error();
        if (error != 122 || needed <= 0 || needed > 8192) throw QueryError(error);
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _)) throw QueryError();
            var config = Marshal.PtrToStructure<ServiceConfig>(buffer);
            return new(status.ProcessId, status.CurrentState, status.ServiceType,
                Marshal.PtrToStringUni(config.BinaryPath) ?? "", Marshal.PtrToStringUni(config.Account) ?? "");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Returns an actionable SCM error without including service configuration or credential data.</summary>
    private static AgentIpcException QueryError(int? nativeError = null)
    {
        var error = nativeError ?? Marshal.GetLastWin32Error();
        return error switch
        {
            1060 => new("service_not_installed", "The CLMS agent service is not installed. Run the service installer, then refresh.", error),
            5 => new("service_query_denied", "Windows denied read access to the CLMS service configuration. Repair its service query permissions.", error),
            _ => new("service_query_failed", $"Windows could not query the CLMS agent service (error {error}).", error)
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode,
            CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfig
    {
        public uint ServiceType, StartType, ErrorControl;
        public IntPtr BinaryPath, LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies, Account, DisplayName;
    }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>Creates the owning handle used by the native SCM marshaler.</summary>
        public ServiceHandle() : base(true) { }
        /// <summary>Closes the native SCM handle when disposed or finalized.</summary>
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    /// <summary>Opens the local SCM for read-only connection access.</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    /// <summary>Opens the registered agent service with explicitly requested query rights.</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    /// <summary>Reads the SCM-maintained running process identity.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int infoLevel, out ServiceStatus status, int size, out int needed);
    /// <summary>Reads the executable command and service account from SCM configuration.</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryServiceConfig(ServiceHandle service, IntPtr buffer, int size, out int needed);
    /// <summary>Releases an SCM or service handle.</summary>
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
}
