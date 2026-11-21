using System.Security.Claims;
using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;

namespace CLMS.Tests;

internal sealed class TestCurrentUser(User? user = null, IPrivilegeResolver? resolver = null) : ICurrentUser
{
    public User User { get; } = user ?? new(Guid.NewGuid(), new Name("Test"), new Name("Actor"), "actor@example.test", Role.Admin, "test-hash");
    public Task<User> GetActorAsync() => !User.DeletedAt.HasValue && !User.IsLockedOut && User.PasswordChanged
        ? Task.FromResult(User) : throw new UnauthorizedAccessException();
    public async Task<Guid> RequirePrivilegeAsync(string privilege)
    {
        await GetActorAsync();
        if (resolver is null ? !Privileges.Defaults(User.UserRole).Contains(privilege) : !await resolver.HasPrivilegeAsync(User.Id, privilege)) throw new UnauthorizedAccessException();
        return User.Id;
    }
    public Task<Guid> RequireSuperAdminAsync() => RequirePrivilegeAsync(Privileges.SecurityManage);
    public async Task<Guid> RequireAdminAsync() { await GetActorAsync(); return Privileges.HasAdministrativeScope(User.UserRole) ? User.Id : throw new UnauthorizedAccessException(); }
    public Task<Guid> RequirePrivilegeForAsync(ClaimsPrincipal principal, string privilege)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(id, out var parsed)
            || !Enum.TryParse<Role>(principal.FindFirstValue(ClaimTypes.Role), out var role) || !Privileges.Defaults(role).Contains(privilege)) throw new UnauthorizedAccessException();
        return Task.FromResult(parsed);
    }
}

internal sealed class TestAuthorizationChanges : IAuthorizationChanges
{
    public int Count;
    public void NotifyChanged() => Count++;
}

/// <summary>Only persistence simulation for non-database tests. Database tests exercise the real transaction guards.</summary>
internal sealed class TestAuthorizationStore(IUserRepository users) : IAuthorizationRepository
{
    public Dictionary<Role, HashSet<string>> Roles = new() { [Role.Admin] = Privileges.Defaults(Role.Admin).ToHashSet(), [Role.Faculty] = Privileges.Defaults(Role.Faculty).ToHashSet() };
    public Dictionary<Guid, Dictionary<string, bool>> Overrides = [];
    public int Denials;
    public int SnapshotReads;
    public Func<Guid, CancellationToken, Task<AuthorizationSnapshot?>>? ReadSnapshot;
    public async Task<AuthorizationSnapshot?> GetAsync(Guid id, CancellationToken ct = default)
    {
        Interlocked.Increment(ref SnapshotReads);
        if (ReadSnapshot is not null) return await ReadSnapshot(id, ct);
        var user = await users.FindByIdAsync(id);
        return user is null ? null : new(id, user.UserRole, !user.DeletedAt.HasValue && !user.IsLockedOut && user.PasswordChanged,
            Roles.GetValueOrDefault(user.UserRole) ?? [], Overrides.GetValueOrDefault(id) ?? []);
    }
    public async Task<IReadOnlySet<string>> GetEffectivePrivilegesAsync(Guid id, CancellationToken ct = default) => (await GetAsync(id, ct))?.Effective ?? new HashSet<string>();
    public async Task<bool> HasPrivilegeAsync(Guid id, string privilege, CancellationToken ct = default) => Privileges.IsKnown(privilege) && (await GetEffectivePrivilegesAsync(id, ct)).Contains(privilege);
    public Task<IReadOnlyList<RoleAccess>> GetRolesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RoleAccess>>([new(Role.Admin, Roles[Role.Admin]), new(Role.Faculty, Roles[Role.Faculty]), new(Role.SuperAdmin, Privileges.Defaults(Role.SuperAdmin))]);
    private async Task Root(Guid actor) { if ((await GetAsync(actor))?.Role != Role.SuperAdmin) { Denials++; throw new UnauthorizedAccessException(); } }
    public async Task SetRolePrivilegesAsync(Guid actorId, Role role, IReadOnlySet<string> privileges, CancellationToken ct = default) { await Root(actorId); Roles[role] = privileges.ToHashSet(); }
    public async Task SetOverrideAsync(Guid actorId, Guid id, string privilege, bool? granted, CancellationToken ct = default)
    {
        await Root(actorId); if ((await GetAsync(id))?.Role == Role.SuperAdmin) throw new UnauthorizedAccessException();
        if (!Overrides.TryGetValue(id, out var changes)) Overrides[id] = changes = [];
        if (granted.HasValue) changes[privilege] = granted.Value; else changes.Remove(privilege);
    }
    public async Task SaveManagedUserAsync(Guid actorId, User user, bool create, CancellationToken ct = default)
    {
        if (user.UserRole == Role.SuperAdmin) await Root(actorId);
        if (create) await users.AddAsync(user); else await users.UpdateAsync(user);
    }
    public async Task DeleteManagedUserAsync(Guid actorId, Guid id, CancellationToken ct = default) { var user = await users.FindByIdAsync(id); if (user is not null) await users.DeleteAsync(user); }
    public Task ProvisionSuperAdminAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RecordDeniedAsync(Guid actorId, string privilege, string target, CancellationToken ct = default) { Denials++; return Task.CompletedTask; }
}

internal sealed class TestPrivilegeHandler : AuthorizationHandler<CLMS.Web.Authentication.PrivilegeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, CLMS.Web.Authentication.PrivilegeRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && Enum.TryParse<Role>(context.User.FindFirstValue(ClaimTypes.Role), out var role)
            && Privileges.Defaults(role).Contains(requirement.Privilege)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
