using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Owns enrollment polling and the connection independently of settings visibility.</summary>
public sealed class AgentRuntimeService(IAgentIdentityRuntime identity, IAgentConnectionTransport connection,
    ILogger<AgentRuntimeService> logger, TimeSpan? retryInterval = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private Task? running;
    public string Status { get; private set; } = "Starting agent";
    public event Action<string>? StatusChanged;

    public async Task<bool> StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            try { await identity.InitializeAsync(CancellationToken.None); }
            catch
            {
                Publish("Cannot read saved enrollment. Check protected storage and open settings for recovery.");
                return false;
            }
            lifetime = new();
            running = RunAsync(lifetime.Token);
            return identity.Status.ComputerUnitId.HasValue
                && identity.Status.Phase is not ("RecoveryRequired" or "StorageError");
        }
        finally { gate.Release(); }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await identity.TickAsync(ct);
                    Publish(identity.Status.Message);
                    if (identity.Status.Phase is "RecoveryRequired" or "StorageError")
                        await connection.DisconnectAsync();
                    else if (identity.Status.Phase == "Connected" && !connection.IsRunning)
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        attempt.CancelAfter(TimeSpan.FromSeconds(20));
                        var token = await identity.GetAccessTokenAsync(attempt.Token);
                        if (!string.IsNullOrEmpty(token))
                            await connection.ConnectAsync(identity.Status.ServerAddress!, token, attempt.Token);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch
                {
                    logger.LogWarning("Agent lifecycle operation failed; retrying with saved enrollment.");
                    Publish("Connection unavailable; retrying automatically. Check the server and certificate trust.");
                }
                var delay = retryInterval ?? (connection.IsRunning ? identity.NextDelay
                    : TimeSpan.FromSeconds(Math.Min(15, identity.NextDelay.TotalSeconds)));
                await Task.Delay(delay, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { await StopCoreAsync(); Publish("Agent paused. Reconnect to resume."); }
        finally { gate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        lifetime?.Cancel();
        try { if (running is not null) await running; }
        finally
        {
            try { await connection.DisconnectAsync(); }
            finally { lifetime?.Dispose(); lifetime = null; running = null; }
        }
    }

    private void Publish(string status) { Status = status; StatusChanged?.Invoke(status); }
    public async ValueTask DisposeAsync() => await StopAsync();
}
