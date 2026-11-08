using CLMS.Agent.Service.Monitoring;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using CLMS.Agent.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Service;

public sealed class LocalControlServer(
    AgentCoordinator coordinator,
    ILogger<LocalControlServer> logger,
    MacAccessMonitor monitoring) : BackgroundService
{
    /// <summary>Starts the platform-specific local control listener for the lifetime of the machine service.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (OperatingSystem.IsWindows()) await RunWindows(stoppingToken);
        else if (OperatingSystem.IsMacOS()) await RunMac(stoppingToken);
    }
    /// <summary>Serves framed named-pipe requests from verified local interactive users. Rejects network logons before executing commands.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task RunWindows(CancellationToken ct)
    {
        var acl = new PipeSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        while (!ct.IsCancellationRequested)
        {
            await using var pipe = NamedPipeServerStreamAcl.Create(AgentIpc.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, acl);
            await pipe.WaitForConnectionAsync(ct);
            try
            {
                await Handle(pipe, ct, () => WindowsPipeClient.IsLocalInteractive(pipe), () => WindowsPipeClient.IsLocalAdministrator(pipe), null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogDebug("Rejected an unverifiable local pipe client.");
            }
        }
    }
    /// <summary>Creates a root-owned Unix socket accessible to the staff group, serves local requests, and removes the socket during shutdown.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private async Task RunMac(CancellationToken ct)
    {
        const string directory = "/var/run/clms-agent";
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        if (Path.Exists(AgentIpc.SocketPath)) File.Delete(AgentIpc.SocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(AgentIpc.SocketPath));
        if (chown(AgentIpc.SocketPath, 0, 20) != 0) throw new IOException("Unable to set local socket ownership."); // macOS staff
        File.SetUnixFileMode(AgentIpc.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
        listener.Listen(8);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var socket = await listener.AcceptAsync(ct);
                if (getpeereid(socket.Handle.ToInt32(), out var uid, out _) != 0) continue;
                await using var stream = new NetworkStream(socket, ownsSocket: false);
                await Handle(stream, ct, null, () => uid == 0, () => MacAccessNative.Peer(socket));
            }
        }
        finally { File.Delete(AgentIpc.SocketPath); }
    }
    /// <summary>Reads and authorizes one bounded request, dispatches supported UI commands, and returns status or sanitized errors without transmitting device credentials.</summary>
    private async Task Handle(Stream stream, CancellationToken ct, Func<bool>? authorize = null,
        Func<bool>? administrator = null, Func<MacPeerInfo>? accessPeer = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stage = "read_request";
        try
        {
            var request = await AgentIpc.ReadAsync<IpcRequest>(stream, timeout.Token);
            stage = "authorize_client";
            // Windows impersonates the security context of the last message read.
            // Verify after reading, before executing any command; reject network logons.
            if (authorize is not null && !authorize())
            {
                await TryError(stream, "The agent accepts local interactive Windows users only.", "ipc_client_rejected", ct);
                return;
            }
            stage = "execute_command";
            IpcResponse response;
            switch (request.Command)
            {
                case "access-event":
                    if (!OperatingSystem.IsMacOS() || accessPeer is null || request.AccessEvent is null)
                    {
                        await TryError(stream, "A macOS graphical-session observation is required.", "access_event_invalid", ct);
                        return;
                    }
                    MacPeerInfo peer;
                    try { peer = accessPeer(); }
                    catch
                    {
                        await TryError(stream, "The graphical-session peer could not be verified.", "access_event_peer_unavailable", ct);
                        return;
                    }
                    var receipt = await monitoring.AcceptAsync(request.AccessEvent, peer, timeout.Token);
                    response = receipt switch
                    {
                        AccessReceipt.Accepted or AccessReceipt.Duplicate => new(),
                        AccessReceipt.Invalid => new(Error: "The observation is invalid or belongs to a previous boot.", ErrorCode: "access_event_invalid"),
                        AccessReceipt.InactiveSession => new(Error: "The originating session is not the primary console.", ErrorCode: "access_session_inactive"),
                        _ => new(Error: "Monitoring request limit reached. Retry later.", ErrorCode: "access_event_rate_limited")
                    };
                    break;
                case "rotate-key":
                    if (administrator is null || !administrator())
                    {
                        await TryError(stream, "An elevated administrator or root is required to rotate device keys.", "administrator_required", ct);
                        return;
                    }
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    await coordinator.RotateKeyAsync(timeout.Token);
                    response = new(coordinator.Status); break;
                case "status": response = new(coordinator.Status); break;
                case "configure":
                    await coordinator.ConfigureAsync(request.ServerAddress ?? "", timeout.Token);
                    response = new(coordinator.Status); break;
                case "laboratories": response = new(coordinator.Status, await coordinator.LaboratoriesAsync(timeout.Token)); break;
                case "register":
                    await coordinator.RegisterAsync(request.LaboratoryId, request.UnitNumber,
                        typeof(LocalControlServer).Assembly.GetName().Version?.ToString() ?? "1.0", timeout.Token);
                    response = new(coordinator.Status); break;
                case "retry": await coordinator.RetryAsync(timeout.Token); response = new(coordinator.Status); break;
                default: response = new(Error: "Unknown local command."); break;
            }
            stage = "write_response";
            await AgentIpc.WriteAsync(stream, response, timeout.Token);
        }
        catch (AgentApiException ex)
        {
            var message = ex.Code switch
            {
                "assignment_occupied" => "This assignment is occupied. Ask the administrator to reset it before registering.",
                "registration_pending" => "A pending request already exists. Wait for it to expire before retrying.",
                "invalid_laboratory" => "The selected laboratory is unavailable. Reload the laboratory list.",
                _ => "The server could not complete the request. Check connectivity and retry."
            };
            await TryError(stream, message, ct);
        }
        catch (Exception ex) when (stage == "authorize_client")
        {
            LogFailure(ex, stage);
            await TryError(stream, "The service could not verify the Windows client identity. Check the service event log for the native error code.", "ipc_client_verification_failed", ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { await TryError(stream, ex.Message, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogFailure(ex, stage);
            await TryError(stream, "The agent service could not complete this request. Check its event log for the failure stage and error code.", "ipc_request_failed", ct);
        }
    }
    /// <summary>Logs only controlled stage names, exception types, and numeric error codes, never exception messages or request contents.</summary>
    private void LogFailure(Exception exception, string stage) => logger.LogWarning(
        "Local control failed at {Stage}: {ExceptionType}; HResult {HResult}; native error {NativeError}",
        stage, exception.GetType().FullName, exception.HResult,
        exception is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : (int?)null);
    /// <summary>Attempts a short, best-effort IPC error reply without allowing disconnected clients to interrupt service operation.</summary>
    private static async Task TryError(Stream stream, string error, CancellationToken ct)
        => await TryError(stream, error, "ipc_request_failed", ct);

    /// <summary>Returns a stable local error code with a bounded, best-effort reply.</summary>
    private static async Task TryError(Stream stream, string error, string code, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try { await AgentIpc.WriteAsync(stream, new IpcResponse(Error: error, ErrorCode: code), timeout.Token); }
        catch (IOException) { }
        catch (OperationCanceledException) { }
    }
    /// <summary>Assigns Unix socket ownership to root and the intended local group.</summary>
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int chown([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);
    /// <summary>Obtains the credentials associated with a connected Unix socket peer.</summary>
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int getpeereid(int socket, out uint uid, out uint gid);
}
