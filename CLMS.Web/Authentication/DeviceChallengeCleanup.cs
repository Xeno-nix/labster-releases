using CLMS.Application.UnitRegistry;
using CLMS.Application.Monitoring;
using Microsoft.Extensions.Options;

namespace CLMS.Web.Authentication;

/// <summary>Prunes proof challenges without making authentication depend on cleanup timing.</summary>
public sealed class DeviceChallengeCleanup(IServiceScopeFactory scopes, ILogger<DeviceChallengeCleanup> logger,
    TimeProvider clock, IOptions<AbnormalActivityOptions> activitySettings) : BackgroundService
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
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AbnormalActivityService>()
                    .CleanupAsync(clock.GetUtcNow().UtcDateTime, activitySettings.Value.RetentionDays, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Abnormal activity retention maintenance was deferred."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
