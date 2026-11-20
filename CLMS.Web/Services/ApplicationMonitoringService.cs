using System.Security.Claims;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Monitoring;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using CLMS.Web.Services.AgentConnections;

namespace CLMS.Web.Services;

public sealed class ApplicationMonitoringService(IUnitMonitoringRepository units,
    IApplicationRegistryRepository registry, ApplicationScanCoordinator scans)
{
    public async Task<ApplicationMonitoringResult> RequestAsync(ClaimsPrincipal viewer, Guid unit, CancellationToken ct = default)
    {
        MonitoringAccess.RequireStaff(viewer);
        // Reuse the same non-deleted laboratory/unit membership source as screen monitoring.
        if (!(await units.GetAllAsync(ct)).Any(u => u.Id == unit))
            throw new UnauthorizedAccessException("This workstation is not available for monitoring.");
        var scan = await scans.RequestAsync(unit).WaitAsync(ct);
        MonitoringAccess.RequireStaff(viewer);
        var ids = scan.Response.ApplicationIds.ToHashSet();
        var detected = scan.Response.Status == ApplicationScanStatus.Success
            ? (await registry.GetAllAsync(ct)).Where(a => a.IsEnabled && ids.Contains(a.Id)
                && ApplicationProcessNames.AppliesTo(a.Platform, scan.Platform))
                .GroupBy(a => ApplicationProcessNames.Normalize(a.ProcessName), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(a => a.Platform == scan.Platform ? 0 : 1).First())
                .OrderByDescending(a => a.FlagForReview).ThenBy(a => a.Name).Select(a => new DetectedApplication(a.Id, a.Name,
                    a.IconPath is not null && ApplicationProcessNames.IsSafeIconPath(a.IconPath)
                        ? a.IconPath : ApplicationProcessNames.DefaultIcon) { FlagForReview = a.FlagForReview }).ToArray()
            : [];
        return new(scan.Response.RequestId, scan.Response.ObservedAt, scan.Response.Status, detected, scan.RefreshAvailableAt);
    }

}
