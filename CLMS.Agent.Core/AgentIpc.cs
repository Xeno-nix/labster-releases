using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Core;

public sealed record IpcRequest(string Command, string? ServerAddress = null, int LaboratoryId = 0, int UnitNumber = 0,
    MacAccessEvent? AccessEvent = null);
public sealed record IpcResponse(AgentStatus? Status = null, IReadOnlyList<RegistrationLaboratory>? Laboratories = null, string? Error = null, string? ErrorCode = null);

public static class AgentIpc
{
    public const string PipeName = "CLMS.Agent.Control.v1";
    public const string SocketPath = "/var/run/clms-agent/control.sock";
    public const int MaxMessageBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Serializes a local message with a four-byte, big-endian length prefix and enforces the IPC size limit.</summary>
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaxMessageBytes) throw new IOException("Local message exceeds the size limit.");
        var size = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, bytes.Length);
        await stream.WriteAsync(size, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }
    /// <summary>Reads an entire framed JSON message, rejecting invalid lengths before allocating the payload buffer.</summary>
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var size = new byte[4];
        await stream.ReadExactlyAsync(size, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(size);
        if (length is < 1 or > MaxMessageBytes) throw new IOException("Invalid local message size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new IOException("Invalid local message.");
    }
    /// <summary>Connects to the local named pipe or Unix socket, verifies the service identity, and exchanges one request within a bounded timeout.</summary>
    public static async Task<IpcResponse> RequestAsync(IpcRequest request, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.Command == "status" ? 3 : request.Command == "rotate-key" ? 120 : 30));
        if (OperatingSystem.IsWindows())
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(timeout.Token);
            VerifyWindowsServer(pipe);
            await WriteAsync(pipe, request, timeout.Token);
            return await ReadAsync<IpcResponse>(pipe, timeout.Token);
        }
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst()) throw new PlatformNotSupportedException();
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), timeout.Token);
        if (getpeereid(socket.Handle.ToInt32(), out var uid, out _) != 0 || uid != 0)
            throw new IOException("The local agent endpoint is not owned by the system service.");
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        await WriteAsync(stream, request, timeout.Token);
        return await ReadAsync<IpcResponse>(stream, timeout.Token);
    }
    /// <summary>Matches the pipe owner to the registered LocalSystem service using read-only SCM queries, without opening its privileged process.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void VerifyWindowsServer(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid))
            throw new AgentIpcException("pipe_owner_unavailable", "Windows could not identify the agent pipe owner.", Marshal.GetLastWin32Error());
        WindowsAgentService.Verify(pid);
    }
    /// <summary>Obtains the Windows process ID owning a connected named pipe for service identity verification.</summary>
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
    /// <summary>Reads Unix socket peer credentials so the client can require a root-owned service.</summary>
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int getpeereid(int socket, out uint uid, out uint gid);
}
