using CLMS.Application.UnitSessions;

namespace CLMS.Web.Hosted;

/// <summary>
/// Server-side safety net: about once a minute, closes any session whose unit
/// has gone silent (power pulled, crash, network death) at its last heartbeat
/// and marks the unit offline.
/// </summary>
public class UnitLogCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<UnitLogCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<UnitLogCleanupService>();

                var closed = await cleanup
                    .CloseStaleSessionsAsync(DateTime.UtcNow, StaleAfter, stoppingToken);

                if (closed > 0)
                    logger.LogWarning("Closed {Count} stale unit session(s) at last heartbeat.", closed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stale unit session cleanup failed.");
            }
        }
    }
}
