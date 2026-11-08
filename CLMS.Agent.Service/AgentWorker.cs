using CLMS.Agent.Core;
using CLMS.Agent.Service.Monitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Service;

public sealed class AgentWorker(AgentCoordinator coordinator, ILogger<AgentWorker> logger) : BackgroundService
{
    /// <summary>Runs coordinator lifecycle steps until shutdown, honoring its retry delays and logging local failures without credential payloads.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await coordinator.TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch
            {
                // Vault/OS failures must never fall back to plaintext or log exception payloads.
                logger.LogWarning("Agent operation failed locally. Check service vault access and installation permissions.");
            }
            await Task.Delay(coordinator.NextDelay, stoppingToken);
        }
    }
}

public sealed class AccessMonitorWorker(ILogger<AccessMonitorWorker> logger, MacAccessMonitor macMonitoring) : BackgroundService
{
    /// <summary>Reads Windows access events from the service-owned checkpoint until shutdown. Leaves macOS event detection outside this Windows-specific monitor.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        await Task.Yield();
        try
        {
            var monitor = new WindowsAccessMonitor(Path.Combine(ServiceVault.StateDirectory, "unit-access-checkpoint.json"),
                e => logger.LogInformation("Workstation access event {Kind} at {OccurredAtUtc}", e.Event, e.OccurredUtc));
            monitor.Initialize();
            while (!stoppingToken.IsCancellationRequested)
            {
                if (monitor.ReadAvailable() < 256) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch { logger.LogWarning("Windows access monitoring is unavailable. Check event-log permissions and Audit Logon configuration."); }
    }
}
