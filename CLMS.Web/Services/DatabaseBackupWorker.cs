using CLMS.Infrastructure.DatabaseMaintenance;
using Microsoft.Extensions.Options;

namespace CLMS.Web.Services;

public sealed class DatabaseBackupWorker(DatabaseBackupService backups, IOptions<DatabaseBackupOptions> settings,
    ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (!options.Enabled) return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var next = options.NextRun(DateTimeOffset.UtcNow);
                logger.LogInformation("Next database backup scheduled for {NextRun}", next);
                var delay = next - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, stoppingToken);
                try { await backups.CreateAsync("scheduled", stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogError(ex, "Scheduled database backup failed; next attempt is tomorrow."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
