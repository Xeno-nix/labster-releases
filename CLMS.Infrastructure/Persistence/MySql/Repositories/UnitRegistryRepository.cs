using System.Globalization;
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
            var lab = key.Kind switch
            {
                RegistryLockKind.Laboratory => int.Parse(key.Key, CultureInfo.InvariantCulture),
                RegistryLockKind.Registration => await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                    "SELECT laboratory_id FROM unit_registrations WHERE registration_id=@Key", new { key.Key }, cancellationToken: cancellationToken)),
                _ => await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                    "SELECT laboratory_id FROM computer_units WHERE computer_unit_id=@Key", new { key.Key }, cancellationToken: cancellationToken))
            };
            if (lab is null) throw key.Kind == RegistryLockKind.Unit ? RegistryException.InvalidCredential() : RegistryException.Unavailable();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                var lockedLab = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                    "SELECT lab_id FROM laboratories WHERE lab_id=@Lab FOR UPDATE", new { Lab = lab }, transaction, cancellationToken: cancellationToken));
                if (lockedLab is null) throw new RegistryException("invalid_laboratory", "Laboratory is unavailable.", 400);
                var now = DateTime.SpecifyKind(await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                    "SELECT UTC_TIMESTAMP(6)", transaction: transaction, cancellationToken: cancellationToken)), DateTimeKind.Utc);
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
            {
                throw new RegistryException("registry_conflict", "The assignment or credential has already been used.");
            }
        }
    }

    public async Task<RegistryPage<RegistryUnit>> UnitsAsync(int? laboratoryId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var c = Connection();
        var args = new { Lab = laboratoryId, Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Offset = (long)(page - 1) * pageSize, Size = pageSize };
        const string filter = " FROM computer_units u JOIN laboratories l ON l.lab_id=u.laboratory_id WHERE u.deleted_at IS NULL AND (@Lab IS NULL OR u.laboratory_id=@Lab) AND (@Search IS NULL OR CAST(u.unit_number AS CHAR) LIKE CONCAT('%',@Search,'%')) ";
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*)" + filter, args, cancellationToken: cancellationToken));
        var rows = (await c.QueryAsync<RegistryUnit>(new CommandDefinition("""
            SELECT u.computer_unit_id AS Id,u.laboratory_id AS LaboratoryId,COALESCE(l.lab_name,'') AS Laboratory,
              u.unit_number AS UnitNumber,u.unit_name AS UnitName,u.hostname AS Hostname,u.operating_system AS OperatingSystem,
              u.agent_version AS AgentVersion,u.is_enabled AS Enabled,u.registered_at AS RegisteredAtUtc,
              u.last_seen_at AS LastSeenUtc,
              (u.is_enabled=1 AND u.last_seen_at >= UTC_TIMESTAMP(6)-INTERVAL 2 MINUTE) AS Online
            """ + filter + " ORDER BY l.lab_name,u.unit_number LIMIT @Size OFFSET @Offset", args, cancellationToken: cancellationToken))).ToList();
        return new(rows, total, page, pageSize);
    }

    public async Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var c = Connection();
        var args = new { Status = status, Offset = (long)(page - 1) * pageSize, Size = pageSize };
        const string effective = "CASE WHEN r.status='Pending' AND r.expires_at<=UTC_TIMESTAMP(6) THEN 'Expired' ELSE r.status END";
        const string filter = " FROM unit_registrations r JOIN laboratories l ON l.lab_id=r.laboratory_id WHERE (@Status IS NULL OR (" + effective + ")=@Status) ";
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*)" + filter, args, cancellationToken: cancellationToken));
        var rows = (await c.QueryAsync<RegistryRegistration>(new CommandDefinition("""
            SELECT r.registration_id AS Id,r.laboratory_id AS LaboratoryId,COALESCE(l.lab_name,'') AS Laboratory,
              r.unit_number AS UnitNumber,r.registration_code AS Code,r.hostname AS Hostname,r.operating_system AS OperatingSystem,
              r.agent_version AS AgentVersion,
            """ + effective + " AS Status,r.created_at AS CreatedAtUtc,r.expires_at AS ExpiresAtUtc,r.decided_at AS DecidedAtUtc,r.key_fingerprint AS KeyFingerprint" +
            filter + " ORDER BY r.created_at DESC,r.registration_id LIMIT @Size OFFSET @Offset", args, cancellationToken: cancellationToken))).ToList();
        return new(rows, total, page, pageSize);
    }

    public async Task<bool> IsAuthorizedAsync(Guid unitId, int generation, Guid credentialId, CancellationToken cancellationToken)
    {
        await using var c = Connection();
        return await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM computer_units u WHERE u.computer_unit_id=@Id AND u.deleted_at IS NULL
              AND u.is_enabled=1 AND u.credential_generation=@Generation AND EXISTS(
                SELECT 1 FROM device_credentials c WHERE c.computer_unit_id=u.computer_unit_id
                AND c.credential_generation=@Generation AND c.credential_id=@Credential
                AND c.revoked_at IS NULL AND c.activated_at IS NOT NULL))
            """, new { Id = unitId.ToString(), Generation = generation, Credential = credentialId.ToString() }, cancellationToken: cancellationToken));
    }

    /// <summary>Bounds cleanup work per pass; expiration checks never depend on this maintenance job.</summary>
    public async Task CleanupChallengesAsync(CancellationToken cancellationToken)
    {
        await using var c = Connection();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM device_auth_challenges WHERE expires_at < UTC_TIMESTAMP(6)-INTERVAL 24 HOUR LIMIT 10000", cancellationToken: cancellationToken));
    }
    private sealed class Transaction(MySqlConnection connection, MySqlTransaction transaction, DateTime now, CancellationToken ct) : IUnitRegistryTransaction
    {
        public DateTime Now => now;
        private CommandDefinition Cmd(string sql, object? args = null) => new(sql, args, transaction, cancellationToken: ct);
        private Task<int> Run(string sql, object? args = null) => connection.ExecuteAsync(Cmd(sql, args));
        public async Task ExpirePendingAsync(int laboratoryId) => await Run(
            "UPDATE unit_registrations SET status='Expired',last_modified_at=@Now WHERE laboratory_id=@laboratoryId AND status='Pending' AND expires_at<=@Now", new { laboratoryId, Now });
        public Task<bool> HasPendingAsync(int laboratoryId, byte unitNumber) => connection.ExecuteScalarAsync<bool>(Cmd(
            "SELECT EXISTS(SELECT 1 FROM unit_registrations WHERE laboratory_id=@laboratoryId AND unit_number=@unitNumber AND status='Pending')", new { laboratoryId, unitNumber }));
        public Task<UnitRegistration?> RegistrationAsync(Guid id) => connection.QuerySingleOrDefaultAsync<UnitRegistration>(Cmd("""
            SELECT registration_id AS Id,laboratory_id AS LaboratoryId,unit_number AS UnitNumber,registration_code AS Code,
             secret_hash AS SecretHash,public_key AS PublicKey,key_fingerprint AS KeyFingerprint,device_credential_id AS DeviceCredentialId,hostname AS Hostname,operating_system AS OperatingSystem,agent_version AS AgentVersion,
             status AS Status,created_at AS CreatedAtUtc,expires_at AS ExpiresAtUtc,decided_at AS DecidedAtUtc,
             decided_by AS DecidedBy,computer_unit_id AS ComputerUnitId,credential_generation AS CredentialGeneration,claimed_at AS ClaimedAtUtc
            FROM unit_registrations WHERE registration_id=@Id FOR UPDATE
            """, new { Id = id.ToString() }));

        public async Task InsertRegistrationAsync(UnitRegistration r) => await Run("""
            INSERT INTO unit_registrations(registration_id,laboratory_id,unit_number,registration_code,secret_hash,public_key,key_fingerprint,hostname,
              operating_system,agent_version,status,created_at,expires_at,last_modified_at)
            VALUES(@Id,@LaboratoryId,@UnitNumber,@Code,@SecretHash,@PublicKey,@KeyFingerprint,@Hostname,@OperatingSystem,@AgentVersion,'Pending',@CreatedAtUtc,@ExpiresAtUtc,@CreatedAtUtc)
            """, new { Id = r.Id.ToString(), r.LaboratoryId, r.UnitNumber, r.Code, r.SecretHash, r.PublicKey, r.KeyFingerprint, r.Hostname, r.OperatingSystem, r.AgentVersion, r.CreatedAtUtc, r.ExpiresAtUtc });
        public async Task SaveRegistrationAsync(UnitRegistration r) => await Run("""
            UPDATE unit_registrations SET status=@Status,decided_by=@DecidedBy,decided_at=@DecidedAtUtc,
              computer_unit_id=@ComputerUnitId,credential_generation=@CredentialGeneration,device_credential_id=@DeviceCredentialId,claimed_at=@ClaimedAtUtc,last_modified_at=@Now
            WHERE registration_id=@Id
            """, new { Id = r.Id.ToString(), Status = r.Status.ToString(), DecidedBy = r.DecidedBy?.ToString(), r.DecidedAtUtc,
                ComputerUnitId = r.ComputerUnitId?.ToString(), r.CredentialGeneration, DeviceCredentialId = r.DeviceCredentialId?.ToString(), r.ClaimedAtUtc, Now });

        private const string UnitColumns = "SELECT computer_unit_id AS Id,unit_name AS UnitName,unit_number AS UnitNumber,laboratory_id AS LaboratoryId,created_at AS CreatedAt,last_modified_at AS LastModifiedAt,deleted_at AS DeletedAt,hostname AS Hostname,operating_system AS OperatingSystem,agent_version AS AgentVersion,is_enabled AS Enabled,credential_generation AS CredentialGeneration,registered_at AS RegisteredAtUtc FROM computer_units ";
        public async Task<ComputerUnit?> AssignmentAsync(int laboratoryId, byte number) => Map(await connection.QuerySingleOrDefaultAsync<UnitRow>(Cmd(UnitColumns + "WHERE laboratory_id=@laboratoryId AND unit_number=@number FOR UPDATE", new { laboratoryId, number })));
        public async Task<ComputerUnit?> UnitAsync(Guid id) => Map(await connection.QuerySingleOrDefaultAsync<UnitRow>(Cmd(UnitColumns + "WHERE computer_unit_id=@Id FOR UPDATE", new { Id = id.ToString() })));
        private static ComputerUnit? Map(UnitRow? r) => r is null ? null : new ComputerUnit(r.Id, r.UnitName, r.UnitNumber, r.LaboratoryId)
        {
            CreatedAt = r.CreatedAt, LastModifiedAt = r.LastModifiedAt, DeletedAt = r.DeletedAt, Hostname = r.Hostname,
            OperatingSystem = r.OperatingSystem, AgentVersion = r.AgentVersion, Enabled = r.Enabled,
            CredentialGeneration = r.CredentialGeneration, RegisteredAtUtc = r.RegisteredAtUtc
        };
        public async Task SaveUnitAsync(ComputerUnit u, bool create) => await Run(create ? """
            INSERT INTO computer_units(computer_unit_id,unit_name,unit_number,laboratory_id,hostname,operating_system,agent_version,
              is_enabled,credential_generation,registered_at,created_at,last_modified_at,deleted_at)
            VALUES(@Id,@UnitName,@UnitNumber,@LaboratoryId,@Hostname,@OperatingSystem,@AgentVersion,@Enabled,@CredentialGeneration,@RegisteredAtUtc,@CreatedAt,@LastModifiedAt,@DeletedAt)
            """ : """
            UPDATE computer_units SET hostname=@Hostname,operating_system=@OperatingSystem,agent_version=@AgentVersion,
             is_enabled=@Enabled,credential_generation=@CredentialGeneration,registered_at=@RegisteredAtUtc,
             last_modified_at=@LastModifiedAt,deleted_at=@DeletedAt WHERE computer_unit_id=@Id
            """, new { Id = u.Id.ToString(), u.UnitName, u.UnitNumber, u.LaboratoryId, u.Hostname, u.OperatingSystem,
                u.AgentVersion, u.Enabled, u.CredentialGeneration, u.RegisteredAtUtc, u.CreatedAt, u.LastModifiedAt, u.DeletedAt });

        private const string CredentialColumns = "SELECT credential_id AS Id,computer_unit_id AS ComputerUnitId,credential_generation AS Generation,public_key AS PublicKey,key_fingerprint AS Fingerprint,algorithm AS Algorithm,created_at AS CreatedAtUtc,activated_at AS ActivatedAtUtc,revoked_at AS RevokedAtUtc,replaced_by_id AS ReplacedById,replaces_id AS ReplacesId FROM device_credentials ";
        public Task<DeviceCredential?> CredentialAsync(Guid id) => connection.QuerySingleOrDefaultAsync<DeviceCredential>(Cmd(
            CredentialColumns + "WHERE credential_id=@Id FOR UPDATE", new { Id = id.ToString() }));
        public Task<DeviceCredential?> CredentialByFingerprintAsync(string fingerprint) => connection.QuerySingleOrDefaultAsync<DeviceCredential>(Cmd(
            CredentialColumns + "WHERE key_fingerprint=@fingerprint FOR UPDATE", new { fingerprint }));
        public async Task InsertCredentialAsync(DeviceCredential c) => await Run("""
            INSERT INTO device_credentials(credential_id,computer_unit_id,credential_generation,public_key,key_fingerprint,algorithm,created_at,activated_at,replaces_id)
            VALUES(@Id,@Unit,@Generation,@PublicKey,@Fingerprint,@Algorithm,@CreatedAtUtc,@ActivatedAtUtc,@Replaces)
            """, new { Id = c.Id.ToString(), Unit = c.ComputerUnitId.ToString(), c.Generation, c.PublicKey, c.Fingerprint,
                c.Algorithm, c.CreatedAtUtc, c.ActivatedAtUtc, Replaces = c.ReplacesId?.ToString() });
        public async Task SaveCredentialAsync(DeviceCredential c) => await Run("""
            UPDATE device_credentials SET activated_at=@ActivatedAtUtc,revoked_at=@RevokedAtUtc,replaced_by_id=@Replaced
            WHERE credential_id=@Id
            """, new { Id = c.Id.ToString(), c.ActivatedAtUtc, c.RevokedAtUtc, Replaced = c.ReplacedById?.ToString() });
        public async Task RevokeCredentialsAsync(Guid unitId) => await Run(
            "UPDATE device_credentials SET revoked_at=COALESCE(revoked_at,@Now) WHERE computer_unit_id=@Id", new { Id = unitId.ToString(), Now });
        public Task<DeviceChallenge?> ChallengeAsync(Guid id) => connection.QuerySingleOrDefaultAsync<DeviceChallenge>(Cmd("""
            SELECT challenge_id AS Id,computer_unit_id AS ComputerUnitId,credential_id AS CredentialId,context_id AS ContextId,
              replacement_credential_id AS ReplacementCredentialId,purpose AS Purpose,audience AS Audience,nonce AS Nonce,
              created_at AS CreatedAtUtc,expires_at AS ExpiresAtUtc,consumed_at AS ConsumedAtUtc,verified_at AS VerifiedAtUtc
            FROM device_auth_challenges WHERE challenge_id=@Id FOR UPDATE
            """, new { Id = id.ToString() }));
        public async Task InsertChallengeAsync(DeviceChallenge c) => await Run("""
            INSERT INTO device_auth_challenges(challenge_id,computer_unit_id,credential_id,context_id,replacement_credential_id,
              purpose,audience,nonce,created_at,expires_at)
            VALUES(@Id,@Unit,@Credential,@Context,@Replacement,@Purpose,@Audience,@Nonce,@CreatedAtUtc,@ExpiresAtUtc)
            """, new { Id = c.Id.ToString(), Unit = c.ComputerUnitId.ToString(), Credential = c.CredentialId.ToString(),
                Context = c.ContextId.ToString(), Replacement = c.ReplacementCredentialId?.ToString(), c.Purpose, c.Audience,
                c.Nonce, c.CreatedAtUtc, c.ExpiresAtUtc });
        public async Task ConsumeChallengeAsync(Guid id) => await Run(
            "UPDATE device_auth_challenges SET consumed_at=@Now WHERE challenge_id=@Id AND consumed_at IS NULL", new { Id = id.ToString(), Now });
        public async Task VerifyChallengeAsync(Guid id) => await Run(
            "UPDATE device_auth_challenges SET verified_at=@Now WHERE challenge_id=@Id AND consumed_at IS NOT NULL", new { Id = id.ToString(), Now });
        public Task<int> OutstandingChallengesAsync(Guid unitId) => connection.ExecuteScalarAsync<int>(Cmd(
            "SELECT COUNT(*) FROM device_auth_challenges WHERE computer_unit_id=@Id AND consumed_at IS NULL AND expires_at>@Now",
            new { Id = unitId.ToString(), Now }));
        public async Task SetOfflineAsync(Guid unitId)
        {
            await Run("UPDATE computer_units SET last_seen_at=NULL WHERE computer_unit_id=@Id", new { Id = unitId.ToString() });
            await Run("UPDATE unit_availability SET is_online=0 WHERE computer_unit_id=@Id", new { Id = unitId.ToString() });
        }
        public async Task HeartbeatAsync(Guid unitId, string? ipAddress)
        {
            await Run("UPDATE computer_units SET last_seen_at=@Now WHERE computer_unit_id=@Id", new { Id = unitId.ToString(), Now });
            await Run("""
                INSERT INTO unit_availability(computer_unit_id,is_online,last_heartbeat,ip_address) VALUES(@Id,1,@Now,@Ip)
                ON DUPLICATE KEY UPDATE is_online=1,last_heartbeat=@Now,ip_address=@Ip
                """, new { Id = unitId.ToString(), Ip = ipAddress, Now });
        }
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
