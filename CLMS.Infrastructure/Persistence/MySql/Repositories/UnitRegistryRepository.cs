using CLMS.Application.UnitRegistry;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using Dapper;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public sealed class UnitRegistryRepository(MySqlConnectionFactory factory) : IUnitRegistryRepository
{
    private MySqlConnection Connection()
    {
        using var template = factory.CreateConnection();
        return new(new MySqlConnectionStringBuilder(template.ConnectionString) { DateTimeKind = MySqlDateTimeKind.Utc }.ConnectionString);
    }
    public async Task<T> TransactionAsync<T>(RegistryLock key, Func<IUnitRegistryTransaction, Task<T>> action, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var connection = Connection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTime.SpecifyKind(await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                    "CALL sp_unit_registry_transaction(@Kind, @Key);", new { Kind = key.Kind.ToString(), key.Key },
                    transaction, cancellationToken: cancellationToken)), DateTimeKind.Utc);
                var result = await action(new Transaction(connection, transaction, now, cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (MySqlException ex) when (ex.Number is 1213 or 1205 or 1062 && attempt < 2)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await Task.Delay(20 * (attempt + 1), cancellationToken);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            { throw new RegistryException("registry_conflict", "The assignment or credential has already been used."); }
            catch (MySqlException ex) when (ex.Number == 30001)
            { throw key.Kind == RegistryLockKind.Unit ? RegistryException.InvalidCredential() : RegistryException.Unavailable(); }
            catch (MySqlException ex) when (ex.Number == 30002)
            { throw new RegistryException("invalid_laboratory", "Laboratory is unavailable.", 400); }
        }
    }

    public async Task<RegistryPage<RegistryUnit>> UnitsAsync(int? laboratoryId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var c = Connection();
        using var results = await c.QueryMultipleAsync(new CommandDefinition(
            "CALL sp_unit_registry_units(@Lab, @Search, @Page, @Size);",
            new { Lab = laboratoryId, Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Page = page, Size = pageSize }, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var rows = (await results.ReadAsync<RegistryUnit>()).ToList();
        return new(rows, total, page, pageSize);
    }

    public async Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var c = Connection();
        using var results = await c.QueryMultipleAsync(new CommandDefinition(
            "CALL sp_unit_registry_registrations(@Status, @Page, @Size);",
            new { Status = status, Page = page, Size = pageSize }, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<int>();
        var rows = (await results.ReadAsync<RegistryRegistration>()).ToList();
        return new(rows, total, page, pageSize);
    }

    public async Task<bool> IsAuthorizedAsync(Guid unitId, int generation, Guid credentialId, CancellationToken cancellationToken)
    {
        await using var c = Connection();
        return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "CALL sp_unit_registry_is_authorized(@Id, @Generation, @Credential);",
            new { Id = unitId.ToString(), Generation = generation, Credential = credentialId.ToString() }, cancellationToken: cancellationToken));
    }

    /// <summary>Bounds cleanup work per pass; expiration checks never depend on this maintenance job.</summary>
    public async Task CleanupChallengesAsync(CancellationToken cancellationToken)
    {
        await using var c = Connection();
        await c.ExecuteAsync(new CommandDefinition("CALL sp_unit_registry_cleanup_challenges();", cancellationToken: cancellationToken));
    }
    private sealed class Transaction(MySqlConnection connection, MySqlTransaction transaction, DateTime now, CancellationToken ct) : IUnitRegistryTransaction
    {
        public DateTime Now => now;
        private CommandDefinition Cmd(string sql, object? args = null) => new(sql, args, transaction, cancellationToken: ct);
        private Task<int> Run(string sql, object? args = null) => connection.ExecuteAsync(Cmd(sql, args));
        public async Task ExpirePendingAsync(int laboratoryId) => await Run("CALL sp_unit_registry_expire_pending(@laboratoryId, @Now);", new { laboratoryId, Now });
        public Task<bool> HasPendingAsync(int laboratoryId, byte unitNumber) => connection.ExecuteScalarAsync<bool>(Cmd("CALL sp_unit_registry_has_pending(@laboratoryId, @unitNumber);", new { laboratoryId, unitNumber }));
        public Task<UnitRegistration?> RegistrationAsync(Guid id) => connection.QuerySingleOrDefaultAsync<UnitRegistration>(Cmd("CALL sp_unit_registry_registration(@Id);", new { Id = id.ToString() }));

        public async Task InsertRegistrationAsync(UnitRegistration r) => await Run("CALL sp_unit_registry_insert_registration(@Id, @LaboratoryId, @UnitNumber, @Code, @SecretHash, @PublicKey, @KeyFingerprint, @Hostname, @OperatingSystem, @AgentVersion, @CreatedAtUtc, @ExpiresAtUtc);", new { Id = r.Id.ToString(), r.LaboratoryId, r.UnitNumber, r.Code, r.SecretHash, r.PublicKey, r.KeyFingerprint, r.Hostname, r.OperatingSystem, r.AgentVersion, r.CreatedAtUtc, r.ExpiresAtUtc });
        public async Task SaveRegistrationAsync(UnitRegistration r) => await Run("CALL sp_unit_registry_save_registration(@Id, @Status, @DecidedBy, @DecidedAtUtc, @ComputerUnitId, @CredentialGeneration, @DeviceCredentialId, @ClaimedAtUtc, @Now);", new { Id = r.Id.ToString(), Status = r.Status.ToString(), DecidedBy = r.DecidedBy?.ToString(), r.DecidedAtUtc,
                ComputerUnitId = r.ComputerUnitId?.ToString(), r.CredentialGeneration, DeviceCredentialId = r.DeviceCredentialId?.ToString(), r.ClaimedAtUtc, Now });

        public async Task<ComputerUnit?> AssignmentAsync(int laboratoryId, byte number) => Map(await connection.QuerySingleOrDefaultAsync<UnitRow>(Cmd("CALL sp_unit_registry_assignment(@laboratoryId, @number);", new { laboratoryId, number })));
        public async Task<ComputerUnit?> UnitAsync(Guid id) => Map(await connection.QuerySingleOrDefaultAsync<UnitRow>(Cmd("CALL sp_unit_registry_unit(@Id);", new { Id = id.ToString() })));
        private static ComputerUnit? Map(UnitRow? r) => r is null ? null : new ComputerUnit(r.Id, r.UnitName, r.UnitNumber, r.LaboratoryId)
        {
            CreatedAt = r.CreatedAt, LastModifiedAt = r.LastModifiedAt, DeletedAt = r.DeletedAt, Hostname = r.Hostname,
            OperatingSystem = r.OperatingSystem, AgentVersion = r.AgentVersion, Enabled = r.Enabled,
            CredentialGeneration = r.CredentialGeneration, RegisteredAtUtc = r.RegisteredAtUtc
        };
        public async Task SaveUnitAsync(ComputerUnit u, bool create) => await Run("CALL sp_unit_registry_save_unit(@Id, @UnitName, @UnitNumber, @LaboratoryId, @Hostname, @OperatingSystem, @AgentVersion, @Enabled, @CredentialGeneration, @RegisteredAtUtc, @CreatedAt, @LastModifiedAt, @DeletedAt, @create);", new { create, Id = u.Id.ToString(), u.UnitName, u.UnitNumber, u.LaboratoryId, u.Hostname, u.OperatingSystem,
                u.AgentVersion, u.Enabled, u.CredentialGeneration, u.RegisteredAtUtc, u.CreatedAt, u.LastModifiedAt, u.DeletedAt });

        public Task<DeviceCredential?> CredentialAsync(Guid id) => connection.QuerySingleOrDefaultAsync<DeviceCredential>(Cmd("CALL sp_unit_registry_credential(@Id);", new { Id = id.ToString() }));
        public Task<DeviceCredential?> CredentialByFingerprintAsync(string fingerprint) => connection.QuerySingleOrDefaultAsync<DeviceCredential>(Cmd("CALL sp_unit_registry_credential_by_fingerprint(@fingerprint);", new { fingerprint }));
        public async Task InsertCredentialAsync(DeviceCredential c) => await Run("CALL sp_unit_registry_insert_credential(@Id, @Unit, @Generation, @PublicKey, @Fingerprint, @Algorithm, @CreatedAtUtc, @ActivatedAtUtc, @Replaces);", new { Id = c.Id.ToString(), Unit = c.ComputerUnitId.ToString(), c.Generation, c.PublicKey, c.Fingerprint,
                c.Algorithm, c.CreatedAtUtc, c.ActivatedAtUtc, Replaces = c.ReplacesId?.ToString() });
        public async Task SaveCredentialAsync(DeviceCredential c) => await Run("CALL sp_unit_registry_save_credential(@Id, @ActivatedAtUtc, @RevokedAtUtc, @Replaced);", new { Id = c.Id.ToString(), c.ActivatedAtUtc, c.RevokedAtUtc, Replaced = c.ReplacedById?.ToString() });
        public async Task RevokeCredentialsAsync(Guid unitId) => await Run("CALL sp_unit_registry_revoke_credentials(@Id, @Now);", new { Id = unitId.ToString(), Now });
        public Task<DeviceChallenge?> ChallengeAsync(Guid id) => connection.QuerySingleOrDefaultAsync<DeviceChallenge>(Cmd("CALL sp_unit_registry_challenge(@Id);", new { Id = id.ToString() }));
        public async Task InsertChallengeAsync(DeviceChallenge c) => await Run("CALL sp_unit_registry_insert_challenge(@Id, @Unit, @Credential, @Context, @Replacement, @Purpose, @Audience, @Nonce, @CreatedAtUtc, @ExpiresAtUtc);", new { Id = c.Id.ToString(), Unit = c.ComputerUnitId.ToString(), Credential = c.CredentialId.ToString(),
                Context = c.ContextId.ToString(), Replacement = c.ReplacementCredentialId?.ToString(), c.Purpose, c.Audience,
                c.Nonce, c.CreatedAtUtc, c.ExpiresAtUtc });
        public async Task ConsumeChallengeAsync(Guid id) => await Run("CALL sp_unit_registry_consume_challenge(@Id, @Now);", new { Id = id.ToString(), Now });
        public async Task VerifyChallengeAsync(Guid id) => await Run("CALL sp_unit_registry_verify_challenge(@Id, @Now);", new { Id = id.ToString(), Now });
        public Task<int> OutstandingChallengesAsync(Guid unitId) => connection.ExecuteScalarAsync<int>(Cmd("CALL sp_unit_registry_outstanding_challenges(@Id, @Now);",
            new { Id = unitId.ToString(), Now }));
        public async Task SetOfflineAsync(Guid unitId) => await Run(
            "CALL sp_unit_registry_set_offline(@Id);", new { Id = unitId.ToString() });
        public async Task HeartbeatAsync(Guid unitId, string? ipAddress) => await Run(
            "CALL sp_unit_registry_heartbeat(@Id, @Ip, @Now);", new { Id = unitId.ToString(), Ip = ipAddress, Now });
    }

    private sealed class UnitRow
    {
        public Guid Id { get; set; }
        public string UnitName { get; set; } = "";
        public byte UnitNumber { get; set; }
        public int LaboratoryId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? Hostname { get; set; }
        public string? OperatingSystem { get; set; }
        public string? AgentVersion { get; set; }
        public bool Enabled { get; set; }
        public int CredentialGeneration { get; set; }
        public DateTime? RegisteredAtUtc { get; set; }
    }
}
