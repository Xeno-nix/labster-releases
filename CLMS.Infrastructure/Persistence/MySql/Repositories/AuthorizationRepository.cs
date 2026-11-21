using CLMS.Application.Authorization;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

/// <summary>Authorization writes serialize on the fixed SuperAdmin role row, never on browser claims.</summary>
public sealed class AuthorizationRepository(MySqlConnectionFactory factory) : IAuthorizationRepository
{
    public async Task<AuthorizationSnapshot?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var result = await Snapshot(c, tx, userId, ct);
        await tx.CommitAsync(ct);
        return result;
    }
    public async Task<IReadOnlySet<string>> GetEffectivePrivilegesAsync(Guid userId, CancellationToken ct = default) =>
        (await GetAsync(userId, ct))?.Effective ?? new HashSet<string>();
    public async Task<bool> HasPrivilegeAsync(Guid userId, string privilege, CancellationToken ct = default) =>
        Privileges.IsKnown(privilege) && (await GetEffectivePrivilegesAsync(userId, ct)).Contains(privilege);
    public async Task RecordDeniedAsync(Guid actorId, string privilege, string target, CancellationToken ct = default)
    {
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await Audit(c, tx, actorId, "Authorization", target, "privilege_denied", privilege, null, null, false, ct);
        await tx.CommitAsync(ct);
    }

    private static async Task<User?> User(MySqlConnection c, MySqlTransaction tx, Guid id, CancellationToken ct, bool forUpdate = false)
    {
        await using var command = new MySqlCommand("SELECT " + UserRepository.Columns + " FROM users WHERE user_id=@id" + (forUpdate ? " FOR UPDATE" : ""), c, tx);
        command.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? UserRepository.ReadUser(reader) : null;
    }
    private static async Task<AuthorizationSnapshot?> Snapshot(MySqlConnection c, MySqlTransaction tx, Guid id, CancellationToken ct)
    {
        var user = await User(c, tx, id, ct);
        if (user is null) return null;
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        await using var roles = new MySqlCommand("SELECT privilege FROM role_privileges WHERE role_id=@role", c, tx);
        roles.Parameters.AddWithValue("@role", (int)user.UserRole);
        await using (var reader = await roles.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) inherited.Add(reader.GetString(0));
        var overrides = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using var changes = new MySqlCommand("SELECT privilege,is_granted FROM user_privilege_overrides WHERE user_id=@id", c, tx);
        changes.Parameters.AddWithValue("@id", id.ToString());
        await using (var reader = await changes.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) overrides[reader.GetString(0)] = reader.GetBoolean(1);
        return new(id, user.UserRole, !user.DeletedAt.HasValue && !user.IsLockedOut && user.PasswordChanged && !string.IsNullOrWhiteSpace(user.PasswordHash) && Privileges.IsStaff(user.UserRole),
            inherited.Where(Privileges.IsKnown).ToHashSet(StringComparer.Ordinal), overrides);
    }
    private static async Task LockSecurity(MySqlConnection c, MySqlTransaction tx, CancellationToken ct)
    {
        await using var gate = new MySqlCommand("SELECT role_id FROM authorization_roles WHERE role_id=2 FOR UPDATE", c, tx);
        if (await gate.ExecuteScalarAsync(ct) is null) throw new InvalidOperationException("Apply the authorization migration first.");
    }
    private static async Task Audit(MySqlConnection c, MySqlTransaction tx, Guid? actor, string targetType, string target,
        string operation, string? privilege, string? before, string? after, bool allowed, CancellationToken ct)
    {
        await using var audit = new MySqlCommand("""
            INSERT INTO authorization_audit (audit_id,actor_id,target_type,target_key,operation,privilege,previous_value,new_value,is_allowed,occurred_at)
            VALUES (@id,@actor,@type,@target,@operation,@privilege,@before,@after,@allowed,UTC_TIMESTAMP(6))
            """, c, tx);
        audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("@actor", (object?)actor?.ToString() ?? DBNull.Value);
        audit.Parameters.AddWithValue("@type", targetType); audit.Parameters.AddWithValue("@target", target);
        audit.Parameters.AddWithValue("@operation", operation); audit.Parameters.AddWithValue("@privilege", (object?)privilege ?? DBNull.Value);
        audit.Parameters.AddWithValue("@before", (object?)before ?? DBNull.Value); audit.Parameters.AddWithValue("@after", (object?)after ?? DBNull.Value);
        audit.Parameters.AddWithValue("@allowed", allowed); await audit.ExecuteNonQueryAsync(ct);
    }
    private static async Task Deny(MySqlConnection c, MySqlTransaction tx, Guid actor, string targetType, string target, string reason, CancellationToken ct)
    {
        await Audit(c, tx, actor, targetType, target, reason, null, null, null, false, ct);
        await tx.CommitAsync(ct);
        throw new UnauthorizedAccessException("This operation is not permitted (" + reason + ").");
    }
    private static async Task<AuthorizationSnapshot> Require(MySqlConnection c, MySqlTransaction tx, Guid actor, string privilege, string targetType, string target, CancellationToken ct)
    {
        await User(c, tx, actor, ct, true);
        var current = await Snapshot(c, tx, actor, ct);
        if (current is null || !current.Effective.Contains(privilege)) await Deny(c, tx, actor, targetType, target, "privilege_required", ct);
        return current!;
    }
    public async Task<IReadOnlyList<RoleAccess>> GetRolesAsync(CancellationToken ct = default)
    {
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct);
        await using var command = new MySqlCommand("SELECT role_id,privilege FROM role_privileges ORDER BY role_id,privilege", c);
        var assigned = new Dictionary<Role, HashSet<string>> { [Role.Admin] = [], [Role.Faculty] = [] };
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (assigned.TryGetValue((Role)reader.GetInt32(0), out var privileges) && Privileges.IsKnown(reader.GetString(1))) privileges.Add(reader.GetString(1));
        return [new(Role.Admin, assigned[Role.Admin]), new(Role.Faculty, assigned[Role.Faculty]), new(Role.SuperAdmin, Privileges.Defaults(Role.SuperAdmin))];
    }
    public async Task SetRolePrivilegesAsync(Guid actorId, Role role, IReadOnlySet<string> privileges, CancellationToken ct = default)
    {
        if (role is not (Role.Admin or Role.Faculty) || privileges.Any(p => !Privileges.IsKnown(p) || Privileges.IsProtected(p))) throw new ArgumentException("Invalid role privileges.");
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await LockSecurity(c, tx, ct); await Require(c, tx, actorId, Privileges.SecurityManage, "Role", role.ToString(), ct);
        var old = new HashSet<string>();
        await using var read = new MySqlCommand("SELECT privilege FROM role_privileges WHERE role_id=@role", c, tx); read.Parameters.AddWithValue("@role", (int)role);
        await using (var reader = await read.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) old.Add(reader.GetString(0));
        foreach (var privilege in old.Except(privileges).Concat(privileges.Except(old)))
        {
            var grant = privileges.Contains(privilege);
            await using var command = new MySqlCommand(grant ? "INSERT INTO role_privileges(role_id,privilege) VALUES(@role,@privilege)"
                : "DELETE FROM role_privileges WHERE role_id=@role AND privilege=@privilege", c, tx);
            command.Parameters.AddWithValue("@role", (int)role); command.Parameters.AddWithValue("@privilege", privilege); await command.ExecuteNonQueryAsync(ct);
            await Audit(c, tx, actorId, "Role", role.ToString(), grant ? "role_privilege_granted" : "role_privilege_removed", privilege, (!grant).ToString(), grant.ToString(), true, ct);
        }
        await tx.CommitAsync(ct);
    }
    public async Task SetOverrideAsync(Guid actorId, Guid userId, string privilege, bool? granted, CancellationToken ct = default)
    {
        if (!Privileges.IsKnown(privilege) || Privileges.IsProtected(privilege)) throw new ArgumentException("Invalid privilege override.");
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await LockSecurity(c, tx, ct); await Require(c, tx, actorId, Privileges.UsersManagePrivileges, "User", userId.ToString(), ct);
        var target = await User(c, tx, userId, ct, true) ?? throw new ArgumentException("User not found.");
        if (!Privileges.IsStaff(target.UserRole) || target.UserRole == Role.SuperAdmin || target.DeletedAt.HasValue)
            await Deny(c, tx, actorId, "User", userId.ToString(), "protected_or_inactive_user", ct);
        await using var read = new MySqlCommand("SELECT is_granted FROM user_privilege_overrides WHERE user_id=@id AND privilege=@privilege", c, tx);
        read.Parameters.AddWithValue("@id", userId.ToString()); read.Parameters.AddWithValue("@privilege", privilege);
        var old = await read.ExecuteScalarAsync(ct);
        await using var command = new MySqlCommand(granted.HasValue ? """
            INSERT INTO user_privilege_overrides(user_id,privilege,is_granted) VALUES(@id,@privilege,@grant)
            ON DUPLICATE KEY UPDATE is_granted=@grant
            """ : "DELETE FROM user_privilege_overrides WHERE user_id=@id AND privilege=@privilege", c, tx);
        command.Parameters.AddWithValue("@id", userId.ToString()); command.Parameters.AddWithValue("@privilege", privilege);
        command.Parameters.AddWithValue("@grant", granted ?? false); await command.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, actorId, "User", userId.ToString(), granted is null ? "override_reset" : granted.Value ? "privilege_granted" : "privilege_revoked",
            privilege, old is null ? "Inherit" : Convert.ToBoolean(old) ? "Grant" : "Revoke", granted is null ? "Inherit" : granted.Value ? "Grant" : "Revoke", true, ct);
        await tx.CommitAsync(ct);
    }
    private static async Task ProtectLast(MySqlConnection c, MySqlTransaction tx, Guid actor, User target, CancellationToken ct)
    {
        if (target.UserRole != Role.SuperAdmin || target.DeletedAt.HasValue) return;
        await using var count = new MySqlCommand("""
            SELECT user_id FROM users WHERE role_type='superadmin' AND user_id<>@id AND deleted_at IS NULL
                AND password_changed=TRUE AND password_hash IS NOT NULL AND password_hash<>'' AND (lockout_end IS NULL OR lockout_end<=UTC_TIMESTAMP())
            ORDER BY user_id FOR UPDATE
            """, c, tx);
        count.Parameters.AddWithValue("@id", target.Id.ToString());
        if (await count.ExecuteScalarAsync(ct) is null)
            await Deny(c, tx, actor, "User", target.Id.ToString(), "last_active_superadmin", ct);
    }
    public async Task SaveManagedUserAsync(Guid actorId, User user, bool create, CancellationToken ct = default)
    {
        if (!Privileges.IsStaff(user.UserRole)) throw new ArgumentException("Select an assignable staff role.");
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await LockSecurity(c, tx, ct);
        var actor = await Require(c, tx, actorId, create ? Privileges.UsersCreate : Privileges.UsersEdit, "User", user.Id.ToString(), ct);
        var previous = await User(c, tx, user.Id, ct, true);
        if (create && previous is not null || !create && (previous is null || previous.DeletedAt.HasValue)) throw new ArgumentException("The user changed. Reload the user list.");
        var roleChanged = create || previous!.UserRole != user.UserRole;
        if ((previous?.UserRole == Role.SuperAdmin || user.UserRole == Role.SuperAdmin) && actor.Role != Role.SuperAdmin)
            await Deny(c, tx, actorId, "User", user.Id.ToString(), "protected_superadmin", ct);
        if (roleChanged)
        {
            await Require(c, tx, actorId, Privileges.UsersAssignRoles, "User", user.Id.ToString(), ct);
            if (previous?.UserRole == Role.SuperAdmin && user.UserRole != Role.SuperAdmin) await ProtectLast(c, tx, actorId, previous, ct);
            if (actor.Role != Role.SuperAdmin)
            {
                var target = await Snapshot(c, tx, user.Id, ct);
                var roles = new HashSet<string>();
                await using var defaults = new MySqlCommand("SELECT privilege FROM role_privileges WHERE role_id=@role", c, tx);
                defaults.Parameters.AddWithValue("@role", (int)user.UserRole);
                await using (var reader = await defaults.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) roles.Add(reader.GetString(0));
                var effective = new AuthorizationSnapshot(user.Id, user.UserRole, true, roles, target?.Overrides ?? new Dictionary<string, bool>()).Effective;
                if (!effective.IsSubsetOf(actor.Effective)) await Deny(c, tx, actorId, "User", user.Id.ToString(), "assignment_exceeds_actor_access", ct);
            }
        }
        await using var command = new MySqlCommand(create ? """
            INSERT INTO users(user_id,first_name,last_name,middle_name,suffix,email,password_hash,role_type,password_changed,two_factor_enabled)
            VALUES(@id,@first,@last,@middle,@suffix,@email,@hash,@role,@changed,@twoFactor)
            """ : """
            UPDATE users SET first_name=@first,last_name=@last,middle_name=@middle,suffix=@suffix,role_type=@role,last_modified_at=UTC_TIMESTAMP()
            WHERE user_id=@id AND deleted_at IS NULL
            """, c, tx);
        command.Parameters.AddWithValue("@id", user.Id.ToString()); command.Parameters.AddWithValue("@first", user.FirstName.Value); command.Parameters.AddWithValue("@last", user.LastName.Value);
        command.Parameters.AddWithValue("@middle", (object?)user.MiddleName?.Value ?? DBNull.Value); command.Parameters.AddWithValue("@suffix", (object?)user.Suffix?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("@email", user.Email); command.Parameters.AddWithValue("@hash", user.PasswordHash); command.Parameters.AddWithValue("@role", user.UserRole.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("@changed", user.PasswordChanged); command.Parameters.AddWithValue("@twoFactor", user.TwoFactorEnabled);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (MySqlException ex) when (ex.Number == 1062) { throw new ArgumentException("A user with this email already exists."); }
        if (roleChanged)
        {
            if (user.UserRole == Role.SuperAdmin) await ClearOverrides(c, tx, actorId, user.Id, ct);
            await RevokeRefresh(c, tx, user.Id, ct);
            await Audit(c, tx, actorId, "User", user.Id.ToString(), "role_assigned", null, previous?.UserRole.ToString(), user.UserRole.ToString(), true, ct);
        }
        await tx.CommitAsync(ct);
    }
    private static async Task ClearOverrides(MySqlConnection c, MySqlTransaction tx, Guid? actor, Guid target, CancellationToken ct)
    {
        await using var read = new MySqlCommand("SELECT privilege,is_granted FROM user_privilege_overrides WHERE user_id=@id", c, tx); read.Parameters.AddWithValue("@id", target.ToString());
        var previous = new Dictionary<string, bool>();
        await using (var reader = await read.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) previous[reader.GetString(0)] = reader.GetBoolean(1);
        foreach (var item in previous) await Audit(c, tx, actor, "User", target.ToString(), "override_cleared_on_promotion", item.Key, item.Value ? "Grant" : "Revoke", "Automatic", true, ct);
        await using var clear = new MySqlCommand("DELETE FROM user_privilege_overrides WHERE user_id=@id", c, tx); clear.Parameters.AddWithValue("@id", target.ToString()); await clear.ExecuteNonQueryAsync(ct);
    }
    private static async Task RevokeRefresh(MySqlConnection c, MySqlTransaction tx, Guid id, CancellationToken ct)
    {
        await using var revoke = new MySqlCommand("UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND revoked_at IS NULL", c, tx);
        revoke.Parameters.AddWithValue("@id", id.ToString()); await revoke.ExecuteNonQueryAsync(ct);
    }
    public async Task DeleteManagedUserAsync(Guid actorId, Guid userId, CancellationToken ct = default)
    {
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await LockSecurity(c, tx, ct);
        var actor = await Require(c, tx, actorId, Privileges.UsersDelete, "User", userId.ToString(), ct);
        var target = await User(c, tx, userId, ct, true);
        if (target is null || target.DeletedAt.HasValue) return;
        if (target.UserRole == Role.SuperAdmin && actor.Role != Role.SuperAdmin) await Deny(c, tx, actorId, "User", userId.ToString(), "protected_superadmin", ct);
        await ProtectLast(c, tx, actorId, target, ct);
        await using var command = new MySqlCommand("UPDATE users SET deleted_at=UTC_TIMESTAMP(),last_modified_at=UTC_TIMESTAMP() WHERE user_id=@id", c, tx);
        command.Parameters.AddWithValue("@id", userId.ToString()); await command.ExecuteNonQueryAsync(ct); await RevokeRefresh(c, tx, userId, ct);
        await Audit(c, tx, actorId, "User", userId.ToString(), "user_deleted", null, target.UserRole.ToString(), null, true, ct);
        await tx.CommitAsync(ct);
    }
    public async Task ProvisionSuperAdminAsync(Guid userId, CancellationToken ct = default)
    {
        await using var c = factory.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await LockSecurity(c, tx, ct);
        await using var count = new MySqlCommand("SELECT COUNT(*) FROM users WHERE role_type='superadmin'", c, tx);
        if (Convert.ToInt32(await count.ExecuteScalarAsync(ct)) != 0) throw new InvalidOperationException("SuperAdmin has already been provisioned.");
        var target = await User(c, tx, userId, ct, true);
        if (target is null || target.UserRole != Role.Admin || target.DeletedAt.HasValue || target.IsLockedOut || !target.PasswordChanged || string.IsNullOrWhiteSpace(target.PasswordHash))
            throw new InvalidOperationException("Select an active Admin who has completed initial password setup.");
        await using var command = new MySqlCommand("UPDATE users SET role_type='superadmin',last_modified_at=UTC_TIMESTAMP() WHERE user_id=@id", c, tx);
        command.Parameters.AddWithValue("@id", userId.ToString()); await command.ExecuteNonQueryAsync(ct);
        await ClearOverrides(c, tx, null, userId, ct); await RevokeRefresh(c, tx, userId, ct);
        await Audit(c, tx, null, "User", userId.ToString(), "operator_bootstrap_superadmin", null, "Admin", "SuperAdmin", true, ct);
        await tx.CommitAsync(ct);
    }
}
