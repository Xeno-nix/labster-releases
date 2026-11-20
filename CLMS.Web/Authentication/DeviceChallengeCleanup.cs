using CLMS.Application.UnitRegistry;

namespace CLMS.Web.Authentication;

/// <summary>Prunes proof challenges without making authentication depend on cleanup timing.</summary>
public sealed class DeviceChallengeCleanup(IServiceScopeFactory scopes, ILogger<DeviceChallengeCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IUnitRegistryRepository>().CleanupChallengesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Device challenge maintenance was deferred."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
