using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Identity.Services;

namespace CLMS.Web.Authentication;

public sealed class PasswordRecoveryWorker(IPasswordRecoveryQueue queue, IServiceScopeFactory scopes,
    ILogger<PasswordRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var address in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PasswordRecoveryService>().SendAsync(address, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Exception text, addresses, links, and provider payloads are deliberately excluded.
                logger.LogWarning("A password recovery email could not be delivered. The user can request another link.");
            }
        }
    }
}
