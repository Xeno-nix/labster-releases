using System.Diagnostics;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

/// <summary>Shared background runtime service; no dependency on MAUI or a visible window.</summary>
public sealed class ApplicationProcessMonitor
{
    private readonly ApplicationPlatform platform;
    private readonly Func<IEnumerable<string>> processes;
    private readonly SemaphoreSlim refresh = new(1, 1);
    private sealed record Cache(Dictionary<string, Guid> Applications, Guid? Revision);
    private Cache? registry;

    public ApplicationProcessMonitor() : this(OperatingSystem.IsWindows() ? ApplicationPlatform.Windows
        : ApplicationPlatform.MacOS, EnumerateProcessNames) { }

    internal ApplicationProcessMonitor(ApplicationPlatform platform, Func<IEnumerable<string>> processes)
    {
        this.platform = platform;
        this.processes = processes;
    }

    public ApplicationPlatform Platform => platform;
    public bool IsInitialized => Volatile.Read(ref registry) is not null;

    // A new enrollment/server must never reuse another server's application identifiers.
    public void Reset() => Volatile.Write(ref registry, null);

    public Task<bool> SynchronizeAsync(Func<CancellationToken, Task<IReadOnlyList<AgentApplication>>> fetch,
        CancellationToken ct = default) => ReplaceAsync(async token => (await fetch(token), null), ct);

    public Task<bool> SynchronizeRegistryAsync(Func<CancellationToken, Task<AgentApplicationRegistry>> fetch,
        CancellationToken ct = default) => ReplaceAsync(async token =>
        {
            var response = await fetch(token);
            return (response.Applications, response.Revision);
        }, ct);

    private async Task<bool> ReplaceAsync(Func<CancellationToken, Task<(IReadOnlyList<AgentApplication> Entries, Guid? Revision)>> fetch,
        CancellationToken ct)
    {
        await refresh.WaitAsync(ct);
        try
        {
            var (entries, revision) = await fetch(ct);
            var next = entries.Where(a => ApplicationProcessNames.AppliesTo(a.Platform, platform))
                .Where(a => ApplicationProcessNames.Normalize(a.ProcessName).Length > 0)
                .OrderBy(a => a.Platform == platform ? 0 : 1).ThenBy(a => a.Id)
                .GroupBy(a => ApplicationProcessNames.Normalize(a.ProcessName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            ct.ThrowIfCancellationRequested();
            Volatile.Write(ref registry, new(next, revision));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; } // Preserve the last successful cache on refresh failure.
        finally { refresh.Release(); }
    }

    public ApplicationScanResponse Scan(Guid requestId)
    {
        var snapshot = Volatile.Read(ref registry);
        if (snapshot is null) return new(requestId, DateTimeOffset.UtcNow, ApplicationScanStatus.RegistryUnavailable, []);
        try
        {
            var ids = new HashSet<Guid>();
            var complete = true;
            foreach (var name in processes())
            {
                if (name.Length == 0) { complete = false; continue; }
                if (snapshot.Applications.TryGetValue(ApplicationProcessNames.Normalize(name), out var id)) ids.Add(id);
            }
            return new(requestId, DateTimeOffset.UtcNow, ApplicationScanStatus.Success, ids.Order().ToArray())
                { IsComplete = complete, RegistryRevision = snapshot.Revision };
        }
        catch { return new(requestId, DateTimeOffset.UtcNow, ApplicationScanStatus.Failed, []); }
    }

    private static IEnumerable<string> EnumerateProcessNames()
    {
        var all = Process.GetProcesses();
        try
        {
            foreach (var process in all)
            {
                string? name = null;
                try { name = process.ProcessName; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                catch (NotSupportedException) { }
                catch (UnauthorizedAccessException) { }
                // An unreadable process cannot prove that a monitored application has ended.
                yield return name ?? "";
            }
        }
        finally { foreach (var process in all) process.Dispose(); }
    }
}
