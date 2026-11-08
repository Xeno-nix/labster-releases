using CLMS.Agent.Core;
using CLMS.Agent.Service;
using CLMS.Agent.Service.Monitoring;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Service entry point: the login companion runs without vault access; the machine
// host owns credentials, the process lock, lifecycle polling, IPC, and monitoring.
if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Windows or macOS is required.");
if (args.Contains("--watch-ui", StringComparer.Ordinal))
{
    if (OperatingSystem.IsMacOS())
    {
        using var lifetime = new CancellationTokenSource();
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal => { signal.Cancel = true; lifetime.Cancel(); });
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, signal => { signal.Cancel = true; lifetime.Cancel(); });
        // Call synchronously before any await: CF distributed notifications require the main run loop.
        MacSessionCompanion.Run(lifetime.Token);
    }
    else await LoginCompanion.RunAsync(CancellationToken.None);
    return;
}
if (args.Contains("--rotate-key", StringComparer.Ordinal))
{
    var result = await AgentIpc.RequestAsync(new("rotate-key"));
    if (result.Error is not null) throw new InvalidOperationException(result.Error);
    return;
}
var vault = new ServiceVault();
IDeviceKeyStore deviceKeys = OperatingSystem.IsWindows() ? new WindowsDeviceKeyStore()
    : OperatingSystem.IsMacOS() ? new MacDeviceKeyStore() : throw new PlatformNotSupportedException();
// Acquire before any worker can read or rotate credentials.
using var processLock = new FileStream(Path.Combine(ServiceVault.StateDirectory, "service.lock"),
    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "CLMS.Agent");
builder.Logging.AddFilter("System.Net.Http", LogLevel.None);
builder.Services.AddSingleton(vault);
builder.Services.AddSingleton<IAgentVault>(sp => sp.GetRequiredService<ServiceVault>());
builder.Services.AddSingleton(_ => new AgentApi(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) }));
builder.Services.AddSingleton(deviceKeys);
builder.Services.AddSingleton<AgentCoordinator>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<LocalControlServer>();
builder.Services.AddSingleton<MacAccessMonitor>();
builder.Services.AddHostedService<AccessMonitorWorker>();
if (args.Contains("--reset", StringComparer.Ordinal))
{
    // Installation directory and OS vault require administrator/root access. Stop the service first.
    var previous = await vault.ReadAsync(CancellationToken.None);
    var retired = new[] { previous?.KeyId, previous?.RetiredKeyId, previous?.Rotation?.NewKeyId }
        .Concat(previous?.RetiredKeys ?? []).Where(x => x is not null).Cast<string>().Distinct().ToArray();
    // A local administrator can reset the machine-protected snapshot, but only the
    // running service identity can delete its signing keys. Journal that cleanup.
    await vault.WriteAsync(new AgentState(RetiredKeys: retired), CancellationToken.None);
    Console.WriteLine("Local registration state reset. Existing server credentials must also be revoked by an administrator.");
    return;
}
await builder.Build().RunAsync();
