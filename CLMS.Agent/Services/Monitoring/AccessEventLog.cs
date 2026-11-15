using System.Text.Json;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Bounded local access observations; never records usernames or authentication state.</summary>
internal sealed class AccessEventLog(string path)
{
    private readonly object sync = new();
    public void Write(string kind, DateTimeOffset time, string source)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length >= 256 * 1024)
                File.Move(path, path + ".previous", true);
            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(path, options);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(new { Kind = kind, OccurredAtUtc = time.ToUniversalTime(), Source = source }));
        }
    }
}
