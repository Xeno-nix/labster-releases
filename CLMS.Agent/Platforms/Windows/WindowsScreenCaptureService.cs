using System.Runtime.InteropServices;
using CLMS.Agent.Models;
using CLMS.Agent.Services;
using global::Windows.Graphics;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;
using global::Windows.Security.Authorization.AppCapabilityAccess;

namespace CLMS.Agent.Platforms.Windows;

/// <summary>One compositor capture session per viewing session; never reads the desktop through GDI.</summary>
public sealed class WindowsScreenCaptureService : IScreenCaptureService, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object frameSync = new();
    private IDirect3DDevice? device;
    private GraphicsCaptureItem? item;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private Direct3D11CaptureFrame? latestFrame;
    private TaskCompletionSource frameAvailable = NewSignal();
    private Exception? captureError;
    private ScreenFrame? lastImage;
    private SizeInt32 bufferSize;
    private nint monitor;
    private bool? borderlessAccess;

    public Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => CaptureCoreAsync(cancellationToken), cancellationToken);

    private async Task<ScreenFrame> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var primary = WindowsGraphicsCaptureInterop.PrimaryMonitor();
            if (session is not null && monitor != primary) StopCore();
            await EnsureSessionAsync(primary, cancellationToken);
            // A display-resolution change requires fresh pool surfaces, not a restart on every frame.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var frame = await TakeFrameAsync(cancellationToken);
                if (frame is null) return lastImage!;
                var size = frame.ContentSize;
                if (size.Width <= 0 || size.Height <= 0)
                {
                    frame.Dispose();
                    throw new InvalidOperationException("The captured display is unavailable.");
                }
                if (size.Width != bufferSize.Width || size.Height != bufferSize.Height)
                {
                    frame.Dispose();
                    StopCore();
                    await EnsureSessionAsync(WindowsGraphicsCaptureInterop.PrimaryMonitor(), cancellationToken);
                    continue;
                }

                using (frame)
                {
                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                    cancellationToken.ThrowIfCancellationRequested();
                    var scale = Math.Min(1d, 1280d / size.Width);
                    var width = Math.Max(1, (int)(size.Width * scale));
                    var height = Math.Max(1, (int)(size.Height * scale));
                    using var stream = new InMemoryRandomAccessStream();
                    var properties = new BitmapPropertySet
                    {
                        ["ImageQuality"] = new BitmapTypedValue(0.7f, global::Windows.Foundation.PropertyType.Single)
                    };
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, properties);
                    encoder.SetSoftwareBitmap(bitmap);
                    encoder.BitmapTransform.ScaledWidth = (uint)width;
                    encoder.BitmapTransform.ScaledHeight = (uint)height;
                    encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                    await encoder.FlushAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    using var reader = new DataReader(stream.GetInputStreamAt(0));
                    var length = checked((uint)stream.Size);
                    await reader.LoadAsync(length);
                    var jpeg = new byte[length];
                    reader.ReadBytes(jpeg);
                    cancellationToken.ThrowIfCancellationRequested();
                    lastImage = new(jpeg, width, height, DateTimeOffset.UtcNow);
                    return lastImage;
                }
            }
            throw new InvalidOperationException("Display settings are changing. Start viewing again once the display is ready.");
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            StopCore();
            throw new UnauthorizedAccessException("Windows denied screen capture. Check screen-capture permissions or organizational policy.", ex);
        }
        catch
        {
            StopCore();
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task EnsureSessionAsync(nint primary, CancellationToken cancellationToken)
    {
        if (session is not null) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || !GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException("Screen monitoring requires Windows 10 version 2004 or later with Windows Graphics Capture support.");
        try
        {
            var hideBorder = await RequestBorderlessAccessAsync(cancellationToken);
            device = WindowsGraphicsCaptureInterop.CreateDevice();
            item = WindowsGraphicsCaptureInterop.CreateItem(primary);
            bufferSize = item.Size;
            if (bufferSize.Width <= 0 || bufferSize.Height <= 0)
                throw new InvalidOperationException("No primary display is available.");
            var newPool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, bufferSize);
            lock (frameSync)
            {
                pool = newPool;
                captureError = null;
                frameAvailable = NewSignal();
            }
            newPool.FrameArrived += OnFrameArrived;
            item.Closed += OnDisplayClosed;
            session = newPool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            // Windows permission and system policy control whether borderless capture is allowed.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) && hideBorder)
                session.IsBorderRequired = false;
            monitor = primary;
            session.StartCapture();
        }
        catch
        {
            StopCore();
            throw;
        }
    }

    private async Task<bool> RequestBorderlessAccessAsync(CancellationToken cancellationToken)
    {
        if (borderlessAccess.HasValue) return borderlessAccess.Value;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            return (borderlessAccess = false).Value;
        try
        {
            var access = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)
                .AsTask(cancellationToken);
            borderlessAccess = access == AppCapabilityAccessStatus.Allowed;
        }
        catch (COMException)
        {
            // A missing capability or policy denial must not break ordinary screen sharing.
            borderlessAccess = false;
        }
        return borderlessAccess.Value;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            var frame = sender.TryGetNextFrame();
            if (frame is null) return;
            lock (frameSync)
            {
                if (!ReferenceEquals(pool, sender))
                {
                    frame.Dispose();
                    return;
                }
                latestFrame?.Dispose();
                latestFrame = frame;
                frameAvailable.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            lock (frameSync)
            {
                if (!ReferenceEquals(pool, sender)) return;
                captureError = ex;
                frameAvailable.TrySetResult();
            }
        }
    }

    private void OnDisplayClosed(GraphicsCaptureItem sender, object args)
    {
        lock (frameSync)
        {
            if (!ReferenceEquals(item, sender)) return;
            captureError = new InvalidOperationException("The captured display was disconnected. Start viewing again after reconnecting it.");
            frameAvailable.TrySetResult();
        }
    }

    private async Task<Direct3D11CaptureFrame?> TakeFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task available;
            lock (frameSync)
            {
                if (captureError is not null) throw new InvalidOperationException("Windows screen capture stopped.", captureError);
                if (latestFrame is not null)
                {
                    var frame = latestFrame;
                    latestFrame = null;
                    frameAvailable = NewSignal();
                    return frame;
                }
                // WGC may emit no new frames on a completely static desktop.
                if (lastImage is not null) return null;
                available = frameAvailable.Task;
            }
            try { await available.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException("Windows did not provide a screen frame. Check that this user is signed in and the display is available.", ex);
            }
        }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { StopCore(); }
        finally { gate.Release(); }
    }

    private void StopCore()
    {
        Direct3D11CaptureFramePool? oldPool;
        Direct3D11CaptureFrame? oldFrame;
        lock (frameSync)
        {
            oldPool = pool;
            pool = null;
            oldFrame = latestFrame;
            latestFrame = null;
            captureError = null;
            frameAvailable.TrySetResult();
        }
        if (oldPool is not null) oldPool.FrameArrived -= OnFrameArrived;
        if (item is not null) item.Closed -= OnDisplayClosed;
        // Stop producing before releasing the outstanding frame, pool and GPU device.
        session?.Dispose();
        session = null;
        oldFrame?.Dispose();
        oldPool?.Dispose();
        item = null;
        device?.Dispose();
        device = null;
        monitor = 0;
        lastImage = null;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask DisposeAsync() => await StopAsync();
}
