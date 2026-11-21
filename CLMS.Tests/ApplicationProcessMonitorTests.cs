using CLMS.Agent.Services;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using Xunit;

namespace CLMS.Tests;

public sealed class ApplicationProcessMonitorTests
{
    [Theory]
    [InlineData(" Chrome.EXE ", "chrome")]
    [InlineData("Google Chrome", "google chrome")]
    [InlineData(" chrome ", "chrome")]
    [InlineData(null, "")]
    public void NormalizesNames(string? value, string expected) => Assert.Equal(expected, ApplicationProcessNames.Normalize(value));

    [Theory]
    [InlineData(ApplicationPlatform.Windows)]
    [InlineData(ApplicationPlatform.MacOS)]
    public async Task MatchesExactlyOnceAndFiltersPlatforms(ApplicationPlatform platform)
    {
        var browser = Guid.NewGuid(); var common = Guid.NewGuid();
        var monitor = new ApplicationProcessMonitor(platform, () => [" CHROME.exe ", "chrome", "helper", "chrome-helper", "unlisted"]);
        await monitor.SynchronizeAsync(_ => Task.FromResult<IReadOnlyList<AgentApplication>>([
            new(browser, "Chrome", platform), new(Guid.NewGuid(), "chrome", ApplicationPlatform.Both),
            new(common, "helper", ApplicationPlatform.Both),
            new(Guid.NewGuid(), "unlisted", platform == ApplicationPlatform.Windows ? ApplicationPlatform.MacOS : ApplicationPlatform.Windows)]));
        var request = Guid.NewGuid();
        var result = monitor.Scan(request);
        Assert.Equal(request, result.RequestId);
        Assert.Equal(ApplicationScanStatus.Success, result.Status);
        Assert.Equal(new[] { browser, common }.Order(), result.ApplicationIds.Order());
    }

    [Fact]
    public async Task InitialFailureIsUnavailableAndEmptyInitializedRegistryIsSuccessful()
    {
        var scans = 0;
        var monitor = new ApplicationProcessMonitor(ApplicationPlatform.Windows, () => { scans++; return ["chrome"]; });
        Assert.Equal(ApplicationScanStatus.RegistryUnavailable, monitor.Scan(Guid.NewGuid()).Status);
        Assert.False(await monitor.SynchronizeAsync(_ => throw new IOException()));
        Assert.False(monitor.IsInitialized);
        Assert.Equal(0, scans);
        Assert.True(await monitor.SynchronizeAsync(_ => Task.FromResult<IReadOnlyList<AgentApplication>>([])));
        var result = monitor.Scan(Guid.NewGuid());
        Assert.Equal(ApplicationScanStatus.Success, result.Status);
        Assert.Empty(result.ApplicationIds);
    }

    [Fact]
    public async Task ReplacementIsAtomicAndFailedRefreshPreservesPreviousCache()
    {
        var previous = Guid.NewGuid(); var next = Guid.NewGuid();
        var monitor = new ApplicationProcessMonitor(ApplicationPlatform.MacOS, () => ["chrome"]);
        await monitor.SynchronizeAsync(_ => Task.FromResult<IReadOnlyList<AgentApplication>>([new(previous, "chrome", ApplicationPlatform.Both)]));
        var fetch = new TaskCompletionSource<IReadOnlyList<AgentApplication>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = monitor.SynchronizeAsync(_ => fetch.Task);
        Assert.Equal(previous, Assert.Single(monitor.Scan(Guid.NewGuid()).ApplicationIds));
        fetch.SetResult([new(next, "Chrome", ApplicationPlatform.MacOS)]);
        Assert.True(await replacement);
        Assert.Equal(next, Assert.Single(monitor.Scan(Guid.NewGuid()).ApplicationIds));
        Assert.False(await monitor.SynchronizeAsync(_ => throw new IOException()));
        Assert.Equal(next, Assert.Single(monitor.Scan(Guid.NewGuid()).ApplicationIds));
        monitor.Reset();
        Assert.Equal(ApplicationScanStatus.RegistryUnavailable, monitor.Scan(Guid.NewGuid()).Status);
    }

    [Fact]
    public async Task EnumerationFailureIsNotAnEmptySuccessfulScan()
    {
        var monitor = new ApplicationProcessMonitor(ApplicationPlatform.Windows, () => throw new InvalidOperationException());
        await monitor.SynchronizeAsync(_ => Task.FromResult<IReadOnlyList<AgentApplication>>([]));
        Assert.Equal(ApplicationScanStatus.Failed, monitor.Scan(Guid.NewGuid()).Status);
    }

    [Fact]
    public async Task AProcessNamePrefixDoesNotMatchAndCancellationKeepsTheLoadedRegistry()
    {
        var id = Guid.NewGuid();
        string[] names = ["chrome-helper", "other"];
        var monitor = new ApplicationProcessMonitor(ApplicationPlatform.Windows, () => names);
        await monitor.SynchronizeAsync(_ => Task.FromResult<IReadOnlyList<AgentApplication>>([new(id, "chrome", ApplicationPlatform.Windows)]));
        var result = monitor.Scan(Guid.NewGuid());
        Assert.Equal(ApplicationScanStatus.Success, result.Status);
        Assert.Empty(result.ApplicationIds);
        using var cancelled = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.SynchronizeAsync(_ =>
        {
            cancelled.Cancel();
            return Task.FromResult<IReadOnlyList<AgentApplication>>([]);
        }, cancelled.Token));
        Assert.True(monitor.IsInitialized);
        names = ["chrome"];
        Assert.Equal(id, Assert.Single(monitor.Scan(Guid.NewGuid()).ApplicationIds));
    }

    [Fact]
    public async Task AScanCarriesRegistryFreshnessAndReportsSkippedProcessesWithoutClassifyingApplications()
    {
        var id = Guid.NewGuid(); var revision = Guid.NewGuid();
        var monitor = new ApplicationProcessMonitor(ApplicationPlatform.Windows, () => ["chrome", ""]);
        await monitor.SynchronizeRegistryAsync(_ => Task.FromResult(new AgentApplicationRegistry(revision,
            [new(id, "chrome", ApplicationPlatform.Windows)])));
        var response = monitor.Scan(Guid.NewGuid());
        Assert.Equal(ApplicationScanStatus.Success, response.Status); Assert.Equal(id, Assert.Single(response.ApplicationIds));
        Assert.False(response.IsComplete); Assert.Equal(revision, response.RegistryRevision);
    }
}
