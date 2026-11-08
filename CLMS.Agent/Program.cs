using CLMS.Agent;
using System.Diagnostics.Eventing.Reader;
using System.Security;

// Console prototype entry point, linked into CLMS.Agent.Console and excluded from
// MAUI. It supports local monitoring and self-tests without server authentication.
if (args.Contains("--self-test")) return AccessEventChecks.Run();
if (args.Contains("--help"))
{
    Console.WriteLine("CLMS.Agent [--unit-name LAB1-PC-01] [--lab \"Computer Lab 1\"] [--state path.json] [--once] [--self-test]");
    return 0;
}
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Workstation access monitoring requires Windows.");
    return 1;
}

string workstation = Environment.MachineName, laboratory = "Not configured";
var statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CLMS", "unit-access-checkpoint.json");
var once = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--once") { once = true; continue; }
    if (args[i] is not ("--unit-name" or "--lab" or "--state") || i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
    {
        Console.Error.WriteLine("Invalid arguments. Use --help for usage.");
        return 1;
    }
    switch (args[i++])
    {
        case "--unit-name": workstation = args[i]; break;
        case "--lab": laboratory = args[i]; break;
        case "--state": statePath = args[i]; break;
    }
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
try
{
    statePath = Path.GetFullPath(statePath);
    Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
    using var stateLock = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Console.WriteLine($"Monitoring workstation {workstation}. Times are PH (UTC+8). Press Ctrl+C to stop.");
    var monitor = new WindowsAccessMonitor(statePath, access => Console.WriteLine(access.Format(workstation, laboratory)));
    monitor.Initialize();
    do
    {
        var count = monitor.ReadAvailable();
        if ((once && count < 256) || shutdown.IsCancellationRequested) break;
        if (count < 256) await Task.Delay(TimeSpan.FromSeconds(2), shutdown.Token);
    } while (!shutdown.IsCancellationRequested);
    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
{
    Console.Error.WriteLine("Windows event-log access denied. For testing, run the agent in an elevated terminal. Sign-in/unlock detection also requires successful Logon auditing. Windows settings were not changed.");
    return 1;
}
catch (Exception ex) when (ex is EventLogException or IOException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Unit access monitoring stopped: {ex.Message}");
    return 1;
}
