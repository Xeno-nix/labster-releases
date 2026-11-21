# Hybrid authorization

Labster currently uses custom human-account authentication and MySQL repositories, rather than ASP.NET Core Identity or EF Core. This feature extends those mechanisms. Device authentication and agent enrollment are unchanged.

## Deployment

1. Back up the database and deploy during the normal maintenance window.
2. Apply `Database/migrations/2026-11-23-hybrid-authorization.sql` after the existing users and refresh-token schema. The migration adds the `superadmin` enum value, role metadata, role defaults, explicit user overrides, and the authorization audit table. Run it once with the normal migration tooling; do not rerun the seed statements after customizing role defaults. No EF migration is involved.
3. Provision the first SuperAdmin using an existing active Admin account that has completed initial password setup. From the published application's directory, with its normal connection configuration available, run:

   ```powershell
   dotnet CLMS.Web.dll --provision-superadmin --user-id <existing-admin-guid>
   ```

   The command runs before the HTTP listener starts, records an operator-bootstrap audit entry, clears any overrides, revokes refresh tokens, and exits. It refuses to run if any SuperAdmin membership already exists, including a deleted account. It creates no account or password and has no public registration endpoint. Supply deployment secrets through the existing configuration mechanism.
4. Start Labster normally and sign in again through the existing authentication flow. Configure role defaults at `/admin/users/roles`; manage individual overrides in user details.

Keep at least two usable SuperAdmin accounts for operational recovery. There is no new deactivation endpoint. Existing automatic login lockout, password setup, MFA, account deletion checks, and session-stamp validation remain in force; SuperAdmin does not bypass them.

## Access resolution

The code-defined catalog is `CLMS.Application/Authorization/Privileges.cs`. Only known privileges can be assigned. Existing accounts retain their single role. Admin and Faculty defaults preserve their existing operational access. Unsupported legacy roles and missing, locked, deleted, or incomplete accounts receive no operational privileges.

For ordinary accounts, effective privileges are `(role defaults union explicit grants) minus explicit revocations`. Only overrides are stored: deleting one restores inheritance. Role-default changes therefore affect inheriting users immediately. SuperAdmin automatically receives every catalog privilege and cannot have overrides. `security.manage`, `users.manage_privileges`, and `backups.restore` are protected SuperAdmin-only boundaries.

Static `Privilege:<identifier>` policies use the existing human principal and live database state. Unknown policies fail closed. Application services also check privileges, including when invoked directly from Blazor. Role claims alone cannot grant access. Existing class ownership and screen-session ownership checks still apply. Agent requests continue to use their existing device policies.

Privilege mutations notify existing in-process Blazor circuits to reevaluate authorization. Each operation resolves current database privileges, without a long-lived privilege cache or requiring sign-out. Role or account security changes invalidate existing sessions through the existing security stamp and revoke refresh tokens, so those changes require normal sign-in again. For multiple web-server processes, database checks remain authoritative, but immediate UI reevaluation notifications are local to each process.

## Protected changes and audit

Only active SuperAdmins can change role defaults, user overrides, or SuperAdmin membership. Other administrators cannot modify protected accounts. Ordinary role assignments additionally require `users.assign_roles` and cannot grant more effective access than the actor holds. SuperAdmin promotion clears overrides with an audit entry for each removal.

Authorization mutations serialize on the protected role metadata row and lock relevant account rows in a database transaction. Deleting or demoting the last usable SuperAdmin is rejected, including concurrent attempts. Audit persistence is part of the same transaction; failed audit writes roll back the change. The `authorization_audit` table records actor, target, operation, privilege, before/after state, outcome, and UTC timestamp. It contains no passwords or tokens. Selected prohibited management attempts are also recorded.

The management API is `/api/admin/authorization`. It uses existing cookie authentication, privilege policies, and antiforgery validation for writes. User privilege responses contain access state, not account password/security fields. `null` override values mean Inherit.

## Backup recovery

Interactive restore requires SuperAdmin. Before touching the live database, Labster imports a backup into a generated temporary validation database, verifies the authorization tables and protected role, and requires a usable SuperAdmin in the backup. A pre-authorization or rootless backup is rejected for online restore. Perform such recoveries offline through the operator's established recovery procedure, apply the migration, and provision/recover administrative access before restarting Labster.

The database service account needs permission to create and drop the generated validation database. The existing pre-restore recovery snapshot, integrity checks, and native MySQL tools remain in use. Authorization is checked again immediately before live import. Restore remains a maintenance operation; it is not a concurrent migration mechanism.

## Verification

Focused tests cover inheritance, grants, revocations/reset, live role defaults, unknown privileges, account validity, protected privileges, direct HTTP authorization/antiforgery, and management UI states. Opt-in MySQL tests apply the real SQL migration to a disposable database, verify protected account transactions, concurrent demotions, and audit rollback. Native backup integration tests validate rejection of legacy and rootless backups before an authorized round trip. No test migrates or provisions the deployed database.
