using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.Versioning;
using CLMS.Agent.Core;
using Xunit;

namespace CLMS.Tests;

[SupportedOSPlatform("windows")]
public sealed class AgentServiceIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingOwnProcessSystemServiceIsAccepted(bool quoted)
    {
        if (!OperatingSystem.IsWindows()) return;
        const string path = @"C:\Program Files\CLMS\Service\CLMS.Agent.Service.exe";
        WindowsAgentService.Validate(new(42, 4, 0x10, quoted ? '"' + path + '"' : path, "LocalSystem"), 42, path);
    }

    [Theory]
    [InlineData(0, 4, 0x10, "LocalSystem", "service_not_running")]
    [InlineData(43, 4, 0x10, "LocalSystem", "service_identity_mismatch")]
    [InlineData(42, 1, 0x10, "LocalSystem", "service_not_running")]
    [InlineData(42, 4, 0x20, "LocalSystem", "service_identity_mismatch")]
    [InlineData(42, 4, 0x10, "LocalService", "service_account_mismatch")]
    public void WrongPidStateTypeOrAccountIsRejected(uint pid, uint state, uint type, string account, string code)
    {
        if (!OperatingSystem.IsWindows()) return;
        const string path = @"C:\Program Files\CLMS\Service\CLMS.Agent.Service.exe";
        Assert.Equal(code, Assert.Throws<AgentIpcException>(() => WindowsAgentService.Validate(new(pid, state, type, path, account), 42, path)).Code);
    }

    [Theory]
    [InlineData(@"C:\Users\Public\CLMS.Agent.Service.exe")]
    [InlineData("\"C:\\Program Files\\CLMS\\Service\\CLMS.Agent.Service.exe\" --unexpected")]
    public void WrongExecutableOrExtraCommandIsRejected(string command)
    {
        if (!OperatingSystem.IsWindows()) return;
        const string path = @"C:\Program Files\CLMS\Service\CLMS.Agent.Service.exe";
        Assert.Equal("service_path_mismatch", Assert.Throws<AgentIpcException>(() =>
            WindowsAgentService.Validate(new(42, 4, 0x10, command, "LocalSystem"), 42, path)).Code);
    }

    [InstalledAgentFact]
    public void InstalledServiceCanBeVerifiedUsingReadOnlyScmQueries()
    {
        if (!OperatingSystem.IsWindows()) return;
        var identity = WindowsAgentService.ReadIdentity();
        WindowsAgentService.Verify(identity.ProcessId);
    }

    [Fact]
    public async Task IdentificationPipeSupportsTheServiceClientIdentityCheck()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "CLMS.Identity.Test." + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        var accept = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await accept;
        var read = AgentIpc.ReadAsync<IpcRequest>(server, timeout.Token);
        await AgentIpc.WriteAsync(client, new IpcRequest("status"), timeout.Token);
        await read;
        using var caller = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(caller);
        var expected = principal.IsInRole(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null)) &&
            !principal.IsInRole(new SecurityIdentifier(WellKnownSidType.NetworkSid, null));
        Assert.Equal(expected, WindowsPipeClient.IsLocalInteractive(server));
        using var restored = WindowsIdentity.GetCurrent();
        Assert.Equal(caller.ImpersonationLevel, restored.ImpersonationLevel);
    }

    [Fact]
    public async Task ErrorCodesRoundTripWithoutBreakingOlderReplies()
    {
        using var stream = new MemoryStream();
        await AgentIpc.WriteAsync(stream, new IpcResponse(Error: "Verification failed.", ErrorCode: "ipc_client_verification_failed"), default);
        stream.Position = 0;
        Assert.Equal("ipc_client_verification_failed", (await AgentIpc.ReadAsync<IpcResponse>(stream, default)).ErrorCode);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<IpcResponse>("{\"error\":\"Old service reply\"}", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.ErrorCode);
    }
}

public sealed class InstalledAgentFactAttribute : FactAttribute
{
    public InstalledAgentFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("CLMS_AGENT_SERVICE_TEST") != "1")
            Skip = "Set CLMS_AGENT_SERVICE_TEST=1 on Windows with the installed service running to verify read-only SCM access.";
    }
}
