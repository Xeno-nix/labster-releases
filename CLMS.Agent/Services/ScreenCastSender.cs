using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Shared capture/send loop; platform differences stay behind IScreenCaptureService.</summary>
public sealed class ScreenCastSender(IScreenCaptureService capture, ILogger<ScreenCastSender> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? sending;
    private Guid? sessionId;

    public bool IsSharing { get; private set; }
    public string Status { get; private set; } = "Not sharing with a viewer";
    public event Action<string>? StatusChanged;

    public async Task StartAsync(HubConnection hub, Guid requestedSession, CancellationToken connectionLifetime)
    {
        await gate.WaitAsync(connectionLifetime);
        try
        {
            await StopCoreAsync();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionLifetime);
            sessionId = requestedSession;
            IsSharing = true;
            SetStatus("Sharing screen with an authorized viewer");
            sending = SendLoopAsync(hub, requestedSession, cancellation.Token);
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(Guid? requestedSession = null)
    {
        await gate.WaitAsync();
        try
        {
            if (requestedSession.HasValue && requestedSession != sessionId) return;
            await StopCoreAsync();
        }
        finally { gate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        cancellation?.Cancel();
        if (sending is not null) await sending;
        cancellation?.Dispose();
        cancellation = null;
        sending = null;
        sessionId = null;
    }

    private async Task SendLoopAsync(HubConnection hub, Guid activeSession, CancellationToken cancellationToken)
    {
        var finalStatus = "Not sharing with a viewer";
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            do
            {
                var frame = await capture.CaptureAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (frame.JpegBytes.Length > ScreenCastProtocol.MaximumFrameBytes)
                    throw new InvalidOperationException("Captured frame exceeds the live-view size limit.");
                var accepted = await hub.InvokeAsync<bool>(ScreenCastProtocol.PublishFrame,
                    new ScreenCastFrame(activeSession, frame.JpegBytes, frame.Width, frame.Height), cancellationToken);
                if (!accepted) break;
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (UnauthorizedAccessException ex)
        {
            finalStatus = ex.Message;
            logger.LogWarning(ex, "Screen-cast capture permission was denied.");
        }
        catch (InvalidOperationException ex)
        {
            finalStatus = ex.Message;
            logger.LogWarning(ex, "Screen capture is unavailable.");
        }
        catch (Exception ex)
        {
            finalStatus = "Screen sharing stopped. Check screen-recording permission, display availability, and the server connection.";
            logger.LogWarning(ex, "Screen-cast sending stopped.");
        }
        finally
        {
            try { await capture.StopAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not release the native screen-capture session."); }
            IsSharing = false;
            SetStatus(finalStatus);
            if (hub.State == HubConnectionState.Connected)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await hub.SendAsync(ScreenCastProtocol.CaptureStopped, activeSession, timeout.Token);
                }
                catch (Exception ex) { logger.LogDebug(ex, "Could not acknowledge the end of screen capture."); }
            }
        }
    }

    private void SetStatus(string value)
    {
        Status = value;
        StatusChanged?.Invoke(value);
    }
}
