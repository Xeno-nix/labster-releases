namespace CLMS.Shared.Dtos;

public sealed record ScreenCastCommand(Guid SessionId);
public sealed record ScreenCastFrame(Guid SessionId, byte[] JpegBytes, int Width, int Height);

public static class ScreenCastProtocol
{
    public const string StartCapture = "StartScreenCast";
    public const string StopCapture = "StopScreenCast";
    public const string PublishFrame = "PublishScreenFrame";
    public const string CaptureStopped = "ScreenCaptureStopped";
    public const string FrameRoutePrefix = "/api/screen-cast";
    public const int MaximumFrameBytes = 512 * 1024;
    // JSON encodes byte arrays as base64, which is larger than the JPEG itself.
    public const int MaximumHubMessageBytes = 768 * 1024;
}
