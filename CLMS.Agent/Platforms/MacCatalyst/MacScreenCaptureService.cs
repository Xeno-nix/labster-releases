using CLMS.Agent.Models;
using CLMS.Agent.Services;
using Foundation;
using ScreenCaptureKit;
using UIKit;

namespace CLMS.Agent.Platforms.MacCatalyst;

/// <summary>Captures a Mac display with ScreenCaptureKit, returning the same JPEG contract as Windows.</summary>
public sealed class MacScreenCaptureService : IScreenCaptureService
{
    public Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // macOS requests screen-recording permission when shareable content is accessed.
            // Re-enumerate so a disconnected display is not retained between captures.
            using var content = await SCShareableContent.GetShareableContentAsync(false, true);
            cancellationToken.ThrowIfCancellationRequested();

            var displays = content.Displays;
            var display = displays.FirstOrDefault(candidate => candidate.Frame.X == 0 && candidate.Frame.Y == 0)
                ?? displays.FirstOrDefault()
                ?? throw new InvalidOperationException("No display is available for screen capture.");

            if (display.Width <= 0 || display.Height <= 0)
                throw new InvalidOperationException("The display has no usable capture dimensions.");

            var scale = Math.Min(1d, 1280d / (double)display.Width);
            var width = Math.Max(1, (int)((double)display.Width * scale));
            var height = Math.Max(1, (int)((double)display.Height * scale));

            using var filter = new SCContentFilter(display, Array.Empty<SCWindow>(), SCContentFilterOption.Exclude);
            using var configuration = new SCStreamConfiguration
            {
                Width = (nuint)width,
                Height = (nuint)height,
                ShowsCursor = false,
                CapturesAudio = false
            };

            // Native screenshot operations cannot be cancelled midway. Await their completion
            // before disposing the native objects, then discard the frame if cancellation was requested.
            using var image = await SCScreenshotManager.CaptureImageAsync(filter, configuration);
            var capturedAt = DateTimeOffset.UtcNow;
            cancellationToken.ThrowIfCancellationRequested();

            using var uiImage = UIImage.FromImage(image);
            using var jpeg = uiImage.AsJPEG(0.7f)
                ?? throw new InvalidOperationException("The captured display could not be encoded as JPEG.");
            var bytes = jpeg.ToArray();
            cancellationToken.ThrowIfCancellationRequested();

            return new ScreenFrame(bytes, checked((int)image.Width), checked((int)image.Height), capturedAt);
        }
        catch (NSErrorException ex) when (ex.Error.Code == (long)SCStreamErrorCode.UserDeclined)
        {
            throw new UnauthorizedAccessException(
                "Screen recording access was denied. Allow CLMS.Agent in System Settings > Privacy & Security > Screen Recording, then restart the app.",
                ex);
        }
    }, cancellationToken);
}
