using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Infrastructure.DatabaseMaintenance;

namespace CLMS.Web.Services;

/// <summary>Human-facing boundary; the existing scheduler still uses the infrastructure service directly.</summary>
public sealed class ManualDatabaseBackups(DatabaseBackupService backups, ICurrentUser actor, IAuthorizationChanges changes)
{
    public async Task<IReadOnlyList<DatabaseBackup>> ListAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.BackupsView);
        return backups.List();
    }
    public async Task<DatabaseBackup> CreateAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.BackupsCreate);
        return await backups.CreateAsync();
    }
    public async Task<DatabaseBackup> RestoreAsync(string name)
    {
        await actor.RequirePrivilegeAsync(Privileges.BackupsRestore);
        var result = await backups.RestoreAsync(name, () => actor.RequirePrivilegeAsync(Privileges.BackupsRestore));
        changes.NotifyChanged();
        return result;
    }
}
