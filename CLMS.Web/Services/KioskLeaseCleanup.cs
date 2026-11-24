using CLMS.Infrastructure.Persistence.MySql;
using Dapper;

namespace CLMS.Web.Services;

public sealed class KioskLeaseCleanup(IServiceScopeFactory scopes, ILogger<KioskLeaseCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await using var db = scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().CreateConnection();
                await db.ExecuteAsync(new CommandDefinition("""
                    UPDATE unit_utilization_logs l JOIN student_kiosk_sessions s ON s.log_id=l.unit_utilization_log_id
                    SET l.time_out=s.expires_at WHERE s.expires_at<=UTC_TIMESTAMP(6) AND l.time_out IS NULL
                    """, cancellationToken: stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Kiosk session cleanup deferred. Check the kiosk migration and database connection."); }
        }
    }
}
