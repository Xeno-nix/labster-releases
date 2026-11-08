namespace CLMS.Agent.Service.Monitoring;

/// <summary>Bounded companion diagnostics in the user's private state directory, since launchd may discard stderr.</summary>
internal static class MacSessionDiagnostics
{
    private static readonly object Gate = new();

    /// <summary>Writes controlled messages only; never accepts an exception, username, session ID, or credential.</summary>
    public static void Warning(string message)
    {
        lock (Gate)
        {
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "CLMS", "Agent");
                Directory.CreateDirectory(directory);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var path = Path.Combine(directory, "session-monitor.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 256 * 1024) File.Move(path, path + ".previous", true);
                var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var stream = new FileStream(path, options);
                using var writer = new StreamWriter(stream);
                writer.WriteLine("{0:O} {1}", DateTimeOffset.UtcNow, message);
            }
            catch { /* Monitoring diagnostics must not prevent the companion from retrying. */ }
        }
        Console.Error.WriteLine(message);
    }
}
