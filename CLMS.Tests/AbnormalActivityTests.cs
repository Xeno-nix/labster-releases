using System.Security.Claims;
using CLMS.Application.DTOs.Monitoring;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using CLMS.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CLMS.Tests;

public sealed class AbnormalActivityTests
{
    private static readonly Guid Unit = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static MonitoredApplication App(string name = "Review app", bool review = true) =>
        new(Guid.NewGuid(), name, name.ToLowerInvariant(), ApplicationPlatform.Windows, null, true, At.AddHours(-1), At.AddHours(-1)) { FlagForReview = review };
    private static ApplicationObservation Observation(IReadOnlyList<MonitoredApplication> registry, int seconds, bool complete = true, params Guid[] ids) =>
        new(Unit, Guid.NewGuid(), At.AddSeconds(seconds), At.AddSeconds(seconds + 1), ApplicationPlatform.Windows, ids, complete)
            { RegistryRevision = ApplicationRegistryRevision.Compute(registry) };
    internal static ClaimsPrincipal Viewer(string role = "Faculty") => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "test"));

    [Fact]
    public void FlaggedDetectionsOpenOnceCloseOnAbsenceAndCanReopenLater()
    {
        var flagged = App(); var normal = App("Normal", false); var disabled = App("Disabled") with { IsEnabled = false };
        var mac = App("Mac") with { Platform = ApplicationPlatform.MacOS };
        MonitoredApplication[] registry = [flagged, normal, disabled, mac];
        var first = Assert.Single(FlaggedApplicationDetection.Changes(Observation(registry, 0, true, flagged.Id, flagged.Id, normal.Id, disabled.Id, mac.Id), registry, []));
        Assert.Equal(flagged.Id, first.ApplicationId); Assert.Equal(flagged.Name, first.Details); Assert.Null(first.EndedAt);
        Assert.Empty(FlaggedApplicationDetection.Changes(Observation(registry, 15, true, flagged.Id), registry, [first]));
        var ended = Assert.Single(FlaggedApplicationDetection.Changes(Observation(registry, 30), registry, [first]));
        Assert.Equal(first.Id, ended.Id); Assert.Equal(first.DetectedAt, ended.DetectedAt); Assert.Equal(At.AddSeconds(31), ended.EndedAt);
        var reopened = Assert.Single(FlaggedApplicationDetection.Changes(Observation(registry, 45, true, flagged.Id), registry, [ended]));
        Assert.NotEqual(first.Id, reopened.Id); Assert.Equal(At.AddSeconds(46), reopened.DetectedAt);
    }

    [Fact]
    public void IncompleteScansCanOpenPositiveDetectionsButCannotProveAnActivityEnded()
    {
        MonitoredApplication[] registry = [App()];
        var activity = Assert.Single(FlaggedApplicationDetection.Changes(Observation(registry, 0, false, registry[0].Id), registry, []));
        Assert.Empty(FlaggedApplicationDetection.Changes(Observation(registry, 15, false), registry, [activity]));
        Assert.NotNull(Assert.Single(FlaggedApplicationDetection.Changes(Observation(registry, 30), registry, [activity])).EndedAt);
    }

    [Fact]
    public void StaleDuplicateAndObsoleteRegistryObservationsDoNotChangeActivities()
    {
        MonitoredApplication[] registry = [App()];
        var observation = Observation(registry, 30, true, registry[0].Id);
        var activity = Assert.Single(FlaggedApplicationDetection.Changes(observation, registry, []));
        Assert.Empty(FlaggedApplicationDetection.Changes(Observation(registry, 15), registry, [activity], observation.RequestedAt));
        Assert.Empty(FlaggedApplicationDetection.Changes(observation with { ApplicationIds = [] }, registry, [activity], observation.RequestedAt));
        Assert.Empty(FlaggedApplicationDetection.Changes(Observation(registry, 45) with { RegistryRevision = Guid.NewGuid() }, registry, [activity]));
        Assert.Empty(FlaggedApplicationDetection.Changes(observation, registry, [activity with { EndedAt = At.AddMinutes(1) }], At.AddMinutes(1)));
        Assert.False(FlaggedApplicationDetection.IsNewer(At.AddTicks(7), At.AddTicks(1))); // MySQL microsecond precision.
    }

    [Fact]
    public void EnablingReviewDoesNotRetroactivelyCreateActivityFromAnEarlierScan()
    {
        var app = App() with { UpdatedAt = At.AddSeconds(20) };
        MonitoredApplication[] registry = [app];
        Assert.Empty(FlaggedApplicationDetection.Changes(Observation(registry, 0, true, app.Id), registry, []));
        Assert.Single(FlaggedApplicationDetection.Changes(Observation(registry, 30, true, app.Id), registry, []));
    }

    [Theory]
    [InlineData(ApplicationScanStatus.Failed)]
    [InlineData(ApplicationScanStatus.AgentUnavailable)]
    [InlineData(ApplicationScanStatus.RegistryUnavailable)]
    [InlineData(ApplicationScanStatus.TimedOut)]
    public async Task FailedUnavailableAndUninitializedScansDoNotReachLifecyclePersistence(ApplicationScanStatus status)
    {
        var registry = new ApplicationMonitoringTests.Registry(); registry.Items.Add(App());
        var repository = new ActivityTestRepository(registry); var notifications = new ActivityTestNotifications();
        var service = new AbnormalActivityService(repository, new ActivityTestUnits(Unit), notifications);
        await service.ObserveAsync(Observation(registry.Items, 0, true, registry.Items[0].Id), ApplicationScanStatus.Success);
        await service.ObserveAsync(Observation(registry.Items, 15), status);
        Assert.Null(Assert.Single(repository.Items).EndedAt); Assert.Equal(1, repository.Observations); Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public async Task QueryRequiresStaffAndPassesOnlyPermittedLaboratoryUnitsToPersistence()
    {
        var unit2 = Guid.NewGuid(); var repository = new ActivityTestRepository(new ApplicationMonitoringTests.Registry());
        var units = new ActivityTestUnits(Unit, unit2); var service = new AbnormalActivityService(repository, units, new ActivityTestNotifications());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPageAsync(new ClaimsPrincipal(), new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPageAsync(Viewer("Student"), new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPageAsync(Viewer(), new(UnitId: Guid.NewGuid())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPageAsync(Viewer(), new(UnitId: unit2, LaboratoryId: 1)));
        Assert.Equal(0, repository.Queries);
        await service.GetPageAsync(Viewer(), new(LaboratoryId: 1));
        Assert.Equal(Unit, Assert.Single(repository.AllowedUnits));
        units.Items.RemoveAll(u => u.Id == Unit);
        Assert.Empty((await service.GetPageAsync(Viewer(), new(LaboratoryId: 1))).Items);
    }

    [Fact]
    public async Task ActiveAndHistoryQueriesRetainSnapshotsAndRetentionRemovesOnlyOldEndedRows()
    {
        var registry = new ApplicationMonitoringTests.Registry(); var app = App(); registry.Items.Add(app);
        var repository = new ActivityTestRepository(registry); var notifications = new ActivityTestNotifications();
        var service = new AbnormalActivityService(repository, new ActivityTestUnits(Unit), notifications);
        await service.ObserveAsync(Observation(registry.Items, 0, true, app.Id), ApplicationScanStatus.Success);
        await service.ObserveAsync(Observation(registry.Items, 15), ApplicationScanStatus.Success);
        await service.ObserveAsync(Observation(registry.Items, 30, true, app.Id), ApplicationScanStatus.Success);
        registry.Items[0] = app with { Name = "Renamed" };
        Assert.Equal(app.Name, (await service.GetPageAsync(Viewer(), new())).Items.Single().Activity.Details);
        Assert.Equal(2, (await service.GetPageAsync(Viewer(), new(ActiveOnly: false))).Items.Count);
        Assert.Single((await service.GetPageAsync(Viewer(), new(ActiveOnly: false, FromUtc: At.AddSeconds(20)))).Items);
        await service.CleanupAsync(At.AddDays(31), 30);
        Assert.Null(Assert.Single(repository.Items).EndedAt);
        Assert.Equal(4, notifications.Count);
    }

    [Fact]
    public async Task OnlyAdminCanChangeReviewFlagAndEnablingItDoesNotInvokeAScan()
    {
        var actor = new Actor(); var repository = new ApplicationMonitoringTests.Registry();
        var registry = new ApplicationRegistryService(repository, actor, new RegistryNotifications(), TimeProvider.System);
        await registry.SaveAsync(null, "Review app", "review", ApplicationPlatform.Both, null, true, flagForReview: true);
        var app = Assert.Single(repository.Items); Assert.True(app.FlagForReview);
        actor.Admin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.SaveAsync(app.Id, app.Name, app.ProcessName, app.Platform, null, true, flagForReview: false));
        Assert.True(Assert.Single(repository.Items).FlagForReview);
        actor.Admin = true;
        await registry.SaveAsync(app.Id, app.Name, app.ProcessName, app.Platform, null, true, flagForReview: false);
        Assert.False(Assert.Single(repository.Items).FlagForReview);
    }

    [Fact]
    public void InvalidationsContainNoDataAndAnUnavailableViewerDoesNotPreventOtherUpdates()
    {
        var updates = new MonitoringUpdates(NullLogger<MonitoringUpdates>.Instance); var count = 0;
        updates.Changed += () => throw new ObjectDisposedException("viewer");
        updates.Changed += () => count++;
        updates.NotifyChanged(); Assert.Equal(1, count);
    }

    private sealed class Actor : ICurrentUser
    {
        public bool Admin = true;
        public Task<Guid> RequireAdminAsync() => Admin ? Task.FromResult(Guid.NewGuid()) : throw new UnauthorizedAccessException();
    }
    private sealed class RegistryNotifications : IApplicationRegistryNotifications
    {
        public Task RegistryChangedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}

internal sealed class ActivityTestNotifications : IAbnormalActivityNotifications
{
    public int Count;
    public void NotifyChanged() => Interlocked.Increment(ref Count);
}

internal sealed class ActivityTestUnits(params Guid[] units) : IUnitMonitoringRepository
{
    public List<UnitMonitoringDTO> Items = units.Select((id, i) => new UnitMonitoringDTO { Id = id, Name = "PC-" + i, LaboratoryId = i + 1, Laboratory = "Lab " + (i + 1) }).ToList();
    public Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UnitMonitoringDTO>>(Items.ToArray());
}

internal sealed class ActivityTestRepository(IApplicationRegistryRepository registry) : IAbnormalActivityRepository
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, DateTime> watermarks = [];
    public List<AbnormalActivity> Items = [];
    public int Observations, Queries;
    public IReadOnlyCollection<Guid> AllowedUnits = [];
    public async Task<bool> ObserveAsync(ApplicationObservation observation, CancellationToken ct = default)
    {
        var applications = await registry.GetAllAsync(ct);
        lock (sync)
        {
            Observations++;
            var previous = watermarks.TryGetValue(observation.UnitId, out var at) ? at : (DateTime?)null;
            var changes = FlaggedApplicationDetection.Changes(observation, applications, Items, previous);
            if (observation.RegistryRevision == ApplicationRegistryRevision.Compute(applications) && FlaggedApplicationDetection.IsNewer(observation.RequestedAt, previous))
                watermarks[observation.UnitId] = observation.RequestedAt;
            foreach (var change in changes) { Items.RemoveAll(a => a.Id == change.Id); Items.Add(change); }
            return changes.Count > 0;
        }
    }
    public Task<AbnormalActivityPage> GetPageAsync(AbnormalActivityQuery query, IReadOnlyCollection<Guid> allowedUnits, CancellationToken ct = default)
    {
        Queries++; AllowedUnits = allowedUnits.ToArray();
        var rows = Items.Where(a => allowedUnits.Contains(a.UnitId) && (!query.ActiveOnly || !a.EndedAt.HasValue)
            && (!query.Type.HasValue || a.Type == query.Type) && (!query.FromUtc.HasValue || a.DetectedAt >= query.FromUtc)
            && (!query.ToUtc.HasValue || a.DetectedAt < query.ToUtc)).OrderByDescending(a => a.DetectedAt).Skip(query.Offset).ToArray();
        return Task.FromResult(new AbnormalActivityPage(rows.Take(AbnormalActivityService.PageSize)
            .Select(a => new AbnormalActivityRow(a, "PC", 1, "Lab")).ToArray(), rows.Length > AbnormalActivityService.PageSize));
    }
    public Task<int> DeleteEndedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        Task.FromResult(Items.RemoveAll(a => a.EndedAt.HasValue && a.EndedAt < cutoffUtc));
}
