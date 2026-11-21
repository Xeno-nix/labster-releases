using CLMS.Application.Identity.Interfaces;
using CLMS.Domain.Entities.UnitManagement;

namespace CLMS.Application.Monitoring;

public sealed class ApplicationRegistryService(IApplicationRegistryRepository repository, ICurrentUser user,
    IApplicationRegistryNotifications notifications, TimeProvider clock)
{
    public async Task<IReadOnlyList<MonitoredApplication>> GetAllAsync(CancellationToken ct = default)
    {
        await user.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.MonitoringConfigure);
        return await repository.GetAllAsync(ct);
    }

    public async Task SaveAsync(Guid? id, string name, string processName, ApplicationPlatform platform,
        string? iconPath, bool enabled, CancellationToken ct = default, bool flagForReview = false)
    {
        await user.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.MonitoringConfigure);
        name = (name ?? "").Trim();
        processName = ApplicationProcessNames.Normalize(processName);
        if (name.Length is < 1 or > 120 || name.Any(char.IsControl))
            throw new ArgumentException("Enter an application name of up to 120 characters.");
        if (processName.Length is < 1 or > 200 || processName.Any(c => char.IsControl(c) || c is '/' or '\\'))
            throw new ArgumentException("Enter a process name of up to 200 characters, without a path.");
        if (!Enum.IsDefined(platform)) throw new ArgumentException("Select a supported platform.");
        if (!ApplicationProcessNames.IsSafeIconPath(iconPath)) throw new ArgumentException("Select an uploaded application icon.");
        var applications = await repository.GetAllAsync(ct);
        var existing = id.HasValue ? applications.FirstOrDefault(a => a.Id == id) : null;
        if (id.HasValue && existing is null) throw new ArgumentException("This application no longer exists.");
        if (applications.Any(a => a.Id != id && a.Platform == platform && ApplicationProcessNames.Normalize(a.ProcessName) == processName))
            throw new ArgumentException("This process is already registered for the selected platform.");
        var now = clock.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(new(id ?? Guid.NewGuid(), name, processName, platform, iconPath, enabled,
            existing?.CreatedAt ?? now, now) { FlagForReview = flagForReview }, ct);
        await notifications.RegistryChangedAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await user.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.MonitoringConfigure);
        await repository.DeleteAsync(id, ct);
        await notifications.RegistryChangedAsync(ct);
    }
}
