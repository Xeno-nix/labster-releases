using CLMS.Application.Identity.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;

namespace CLMS.Application.Authorization;

public sealed record AuthorizationSnapshot(Guid UserId, Role Role, bool IsActive,
    IReadOnlySet<string> Inherited, IReadOnlyDictionary<string, bool> Overrides)
{
    public IReadOnlySet<string> Effective => !IsActive || !Privileges.IsStaff(Role) ? new HashSet<string>()
        : Role == Role.SuperAdmin ? Privileges.Defaults(Role.SuperAdmin)
        : Inherited.Concat(Overrides.Where(p => p.Value).Select(p => p.Key))
            .Where(p => Privileges.IsKnown(p) && !Privileges.IsProtected(p) && (!Overrides.TryGetValue(p, out var grant) || grant))
            .ToHashSet(StringComparer.Ordinal);
}

public sealed record RoleAccess(Role Role, IReadOnlySet<string> Privileges);

public interface IPrivilegeResolver
{
    Task<AuthorizationSnapshot?> GetAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetEffectivePrivilegesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> HasPrivilegeAsync(Guid userId, string privilege, CancellationToken ct = default);
}

public interface IAuthorizationRepository : IPrivilegeResolver
{
    Task<IReadOnlyList<RoleAccess>> GetRolesAsync(CancellationToken ct = default);
    Task SetRolePrivilegesAsync(Guid actorId, Role role, IReadOnlySet<string> privileges, CancellationToken ct = default);
    Task SetOverrideAsync(Guid actorId, Guid userId, string privilege, bool? granted, CancellationToken ct = default);
    Task SaveManagedUserAsync(Guid actorId, User user, bool create, CancellationToken ct = default);
    Task DeleteManagedUserAsync(Guid actorId, Guid userId, CancellationToken ct = default);
    Task ProvisionSuperAdminAsync(Guid userId, CancellationToken ct = default);
    Task RecordDeniedAsync(Guid actorId, string privilege, string target, CancellationToken ct = default);
}

public interface IAuthorizationChanges { void NotifyChanged(); }

public sealed class PrivilegeManagementService(IAuthorizationRepository repository, ICurrentUser actor, IAuthorizationChanges changes)
{
    public async Task<IReadOnlyList<RoleAccess>> RolesAsync(CancellationToken ct = default)
    {
        await actor.RequireSuperAdminAsync();
        return await repository.GetRolesAsync(ct);
    }
    public async Task<AuthorizationSnapshot?> UserAsync(Guid userId, CancellationToken ct = default)
    {
        await actor.RequirePrivilegeAsync(Privileges.UsersView);
        return await repository.GetAsync(userId, ct);
    }
    public async Task SetRoleAsync(Role role, IEnumerable<string> privileges, CancellationToken ct = default)
    {
        var actorId = (await actor.GetActorAsync()).Id;
        var selected = privileges.ToHashSet(StringComparer.Ordinal);
        if (role is not (Role.Admin or Role.Faculty) || selected.Any(p => !Privileges.IsKnown(p) || Privileges.IsProtected(p)))
            throw new ArgumentException("Select an ordinary role and defined operational privileges.");
        await repository.SetRolePrivilegesAsync(actorId, role, selected, ct);
        changes.NotifyChanged();
    }
    public async Task SetOverrideAsync(Guid userId, string privilege, bool? granted, CancellationToken ct = default)
    {
        var actorId = (await actor.GetActorAsync()).Id;
        if (!Privileges.IsKnown(privilege) || Privileges.IsProtected(privilege)) throw new ArgumentException("This privilege cannot be overridden.");
        await repository.SetOverrideAsync(actorId, userId, privilege, granted, ct);
        changes.NotifyChanged();
    }
}
