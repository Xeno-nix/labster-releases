using System.Security.Claims;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Monitoring;

public sealed record ApplicationObservation(Guid UnitId, Guid RequestId, DateTime RequestedAt, DateTime ReceivedAt,
    ApplicationPlatform Platform, IReadOnlyCollection<Guid> ApplicationIds, bool IsComplete)
{
    public Guid? RegistryRevision { get; init; }
}

public sealed record AbnormalActivityRow(AbnormalActivity Activity, string UnitName, int LaboratoryId, string Laboratory);
public sealed record AbnormalActivityQuery(bool ActiveOnly = true, Guid? UnitId = null, int? LaboratoryId = null,
    AbnormalActivityType? Type = null, DateTime? FromUtc = null, DateTime? ToUtc = null, int Offset = 0);
public sealed record AbnormalActivityPage(IReadOnlyList<AbnormalActivityRow> Items, bool HasMore);

public interface IAbnormalActivityRepository
{
    // Atomically applies a newer successful observation; returns whether activity rows changed.
    Task<bool> ObserveAsync(ApplicationObservation observation, CancellationToken ct = default);
    Task<AbnormalActivityPage> GetPageAsync(AbnormalActivityQuery query, IReadOnlyCollection<Guid> allowedUnits, CancellationToken ct = default);
    Task<int> DeleteEndedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}

public interface IAbnormalActivityNotifications
{
    // Invalidation only; recipients must reauthorize and query their permitted units.
    void NotifyChanged();
}

public sealed class AbnormalActivityOptions
{
    public int RetentionDays { get; set; } = 30;
}

public static class MonitoringAccess
{
    public static void RequireStaff(ClaimsPrincipal viewer)
    {
        if (viewer.Identity?.IsAuthenticated != true || !Guid.TryParse(viewer.FindFirst(ClaimTypes.NameIdentifier)?.Value, out _)
            || !(viewer.IsInRole("Admin") || viewer.IsInRole("Faculty")))
            throw new UnauthorizedAccessException("Only authorized instructors and administrators can view monitoring data.");
    }
}

public sealed class AbnormalActivityService(IAbnormalActivityRepository repository, IUnitMonitoringRepository units,
    IAbnormalActivityNotifications notifications)
{
    public const int PageSize = 50;

    // Called for an accepted authenticated scan independently of any individual viewer's lifetime.
    public async Task ObserveAsync(ApplicationObservation observation, ApplicationScanStatus status, CancellationToken ct = default)
    {
        if (status != ApplicationScanStatus.Success) return;
        if (await repository.ObserveAsync(observation, ct)) notifications.NotifyChanged();
    }

    public async Task<AbnormalActivityPage> GetPageAsync(ClaimsPrincipal viewer, AbnormalActivityQuery query, CancellationToken ct = default)
    {
        MonitoringAccess.RequireStaff(viewer);
        if (query.Offset < 0 || query.Offset > 100000 || (query.Type.HasValue && !Enum.IsDefined(query.Type.Value))
            || (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc >= query.ToUtc))
            throw new ArgumentException("Select a valid activity filter and date range.");
        var permitted = await units.GetAllAsync(ct);
        // The same non-deleted unit/laboratory source is used by application and screen monitoring.
        if (query.UnitId.HasValue && !permitted.Any(u => u.Id == query.UnitId && (!query.LaboratoryId.HasValue || u.LaboratoryId == query.LaboratoryId)))
            throw new UnauthorizedAccessException("This workstation is not available for monitoring.");
        var allowed = permitted.Where(u => !query.LaboratoryId.HasValue || u.LaboratoryId == query.LaboratoryId)
            .Where(u => !query.UnitId.HasValue || u.Id == query.UnitId).Select(u => u.Id).ToArray();
        if (allowed.Length == 0) return new([], false);
        return await repository.GetPageAsync(query, allowed, ct);
    }

    public async Task CleanupAsync(DateTime nowUtc, int retentionDays, CancellationToken ct = default)
    {
        if (retentionDays is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        if (await repository.DeleteEndedBeforeAsync(nowUtc.AddDays(-retentionDays), ct) > 0) notifications.NotifyChanged();
    }
}
