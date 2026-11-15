namespace CLMS.Agent.Models;

public sealed record ScreenFrame(byte[] JpegBytes, int Width, int Height, DateTimeOffset CapturedAt);
