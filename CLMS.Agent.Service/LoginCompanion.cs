using System.Diagnostics;
using CLMS.Agent.Core;

namespace CLMS.Agent.Service;

public static class LoginCompanion
{
    // Runs in the interactive user's session. It has no vault access and never obtains tokens.
    /// <summary>Polls service status in the signed-in user's session and opens MAUI when registration needs attention. Does not read the machine vault or receive tokens.</summary>
    public static async Task RunAsync(CancellationToken ct)
    {
        string? lastPrompt = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await AgentIpc.RequestAsync(new("status"), ct);
                var phase = response.Status?.Phase;
                if (phase is "RegistrationRequired" or "Pending" or "Rejected" or "Expired" or "RecoveryRequired" or "StorageError")
                {
                    var prompt = phase + ":" + response.Status!.RegistrationCode;
                    if (lastPrompt != prompt)
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            var running = false;
                            foreach (var process in Process.GetProcessesByName("CLMS.Agent"))
                            {
                                using (process) if (process.SessionId == Process.GetCurrentProcess().SessionId) running = true;
                            }
                            if (!running) Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CLMS", "UI", "CLMS.Agent.exe")) { UseShellExecute = true });
                        }
                        else
                        {
                            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                            start.ArgumentList.Add("/Applications/CLMS.Agent.app");
                            Process.Start(start);
                        }
                        lastPrompt = prompt;
                    }
                }
                else if (phase == "Connected") lastPrompt = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* Service might be starting or upgrading. Retry without displaying exception details. */ }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}
