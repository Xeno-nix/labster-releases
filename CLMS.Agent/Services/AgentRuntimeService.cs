using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Owns the connection independently of any settings window.</summary>
public sealed class AgentRuntimeService(
    IAgentSetupStore setup,
    IAgentConnectionTransport connection,
    ILogger<AgentRuntimeService> logger,
    TimeSpan? retryInterval = null) : IAsyncDisposable
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
            string? key;
            string server;
            try
            {
                server = setup.ServerAddress;
                key = await setup.GetDeviceKeyAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to load agent setup.");
                Publish("Cannot read saved setup. Open settings and save the device key again.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                Publish("Setup required. Enter the server address and device key.");
                return false;
            }

            lifetime = new CancellationTokenSource();
            running = RunAsync(server, key, lifetime.Token);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            Publish("Agent paused. Click Reconnect to resume.");
        }
        finally { gate.Release(); }
    }

    private async Task RunAsync(string server, string key, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!connection.IsRunning)
                {
                    Publish("Connecting to server");
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        attempt.CancelAfter(TimeSpan.FromSeconds(20));
                        await connection.ConnectAsync(server, key, attempt.Token);
                        Publish("Agent running. Waiting for an authorized web viewer.");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Agent connection failed; another attempt is scheduled.");
                        Publish("Connection unavailable; retrying automatically. " + AgentConnectionErrors.Describe(ex));
                    }
                }
                await Task.Delay(retryInterval ?? TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task StopCoreAsync()
    {
        lifetime?.Cancel();
        try
        {
            if (running is not null) await running;
            await connection.DisconnectAsync();
        }
        finally
        {
            lifetime?.Dispose();
            lifetime = null;
            running = null;
        }
    }

    private void Publish(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
