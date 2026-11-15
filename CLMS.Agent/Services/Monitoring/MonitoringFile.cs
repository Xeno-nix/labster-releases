using System.Text.Json;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Bounded, atomic monitoring snapshots, separate from the service's authentication vault.</summary>
internal static class MonitoringFile
{
    private const int MaximumBytes = 512 * 1024;

    /// <summary>Reads a snapshot or returns null on first use. A damaged checkpoint must not silently replay activity.</summary>
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumBytes) throw new InvalidDataException("Monitoring checkpoint exceeds its limit.");
        return JsonSerializer.Deserialize<T>(file) ?? throw new InvalidDataException("Invalid monitoring checkpoint.");
    }

    /// <summary>Flushes a private temporary file before atomically replacing the snapshot in the same directory.</summary>
    public static void Write<T>(string path, T state)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A checkpoint directory is required.");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Monitoring checkpoint exceeds its limit.");
        var temporary = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(temporary, options)) { file.Write(bytes); file.Flush(true); }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }
}
