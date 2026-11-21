namespace CLMS.Shared.Dtos;

/// <summary>Machine-wide traffic averaged over the agent's sampling interval, in bytes per second.</summary>
public sealed record NetworkResourceReading(double DownloadBytesPerSecond, double UploadBytesPerSecond)
{
    public bool IsValid => double.IsFinite(DownloadBytesPerSecond) && DownloadBytesPerSecond >= 0
        && double.IsFinite(UploadBytesPerSecond) && UploadBytesPerSecond >= 0;
}
