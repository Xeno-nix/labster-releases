using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.UnitRegistry;
using CLMS.Infrastructure.Auth;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Infrastructure.Repositories;
using CLMS.Shared.Dtos;
using CLMS.Shared.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace CLMS.Tests;

public sealed class UnitRegistryIntegrationTests : IAsyncLifetime
{
    private static readonly Guid Administrator = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private string database = "", root = "";
    private MySqlConnection? admin;
    private MySqlConnectionFactory factory = null!;
    private UnitRegistryRepository repository = null!;
    private UnitRegistryService registry = null!;
    private DeviceAuthenticationService authentication = null!;
    private readonly AgentTokenService tokens = new(Options.Create(new JwtOptions
    { Issuer = "registry-tests", Audience = "humans", Key = new string('K', 64), SigningAlgorithm = "HS256" }), Options.Create(new AgentAuthOptions()));
    private readonly DeviceProofVerifier proofs = new();

    public async Task InitializeAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "CLMS.slnx"))) directory = directory.Parent!;
        root = directory.FullName;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "CLMS.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).Build();
        var connection = Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL") ?? config.GetConnectionString("DefaultConnection");
        var cs = new MySqlConnectionStringBuilder(connection ?? throw new InvalidOperationException("Configure CLMS_TEST_MYSQL."))
        { Database = "", AllowUserVariables = true, ConnectionTimeout = 5 };
        admin = new(cs.ConnectionString);
        await admin.OpenAsync();
        database = "clms_registry_test_" + Guid.NewGuid().ToString("N");
        await Sql(admin, $"CREATE DATABASE `{database}`");
        cs.Database = database;
        factory = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = cs.ConnectionString }).Build());
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await Sql(c, "CREATE TABLE users(user_id CHAR(36) PRIMARY KEY) ENGINE=InnoDB");
        foreach (var table in new[] { "laboratories", "computer_units", "unit_availability", "agent_refresh_credentials", "device_credentials", "device_auth_challenges", "unit_registrations" })
            await Script(c, Path.Combine(root, "Database/clms/" + table + ".sql"));
        await Sql(c, "INSERT INTO laboratories VALUES(1,'Laboratory 1'),(2,'Laboratory 2'); INSERT INTO users VALUES('00000000-0000-0000-0000-000000000001'); CREATE TABLE unit_history(id INT PRIMARY KEY,computer_unit_id CHAR(36),FOREIGN KEY(computer_unit_id) REFERENCES computer_units(computer_unit_id)) ENGINE=InnoDB;");
        repository = new(factory);
        registry = new(repository, new LaboratoryRepository(factory), new RefreshTokenHasher(), tokens, new AdminActor(), proofs);
        authentication = new(repository, registry, proofs, tokens);
    }

    [RegistryDatabaseFact]
    public async Task ApprovalAndPossessionAreRequiredAndCompletionIsOneTime()
    {
        using var key = Key();
        var r = await Register(key);
        Assert.Equal("Pending", (await registry.StatusAsync(r.Id, r.Secret)).Status);
        await Reject(() => registry.StatusAsync(r.Id, r.Code), 404);
        await Reject(() => authentication.CompletionChallengeAsync(r.Id, r.Secret), 409);
        await Reject(() => registry.DecideAsync(r.Id, true, false), 400);
        await registry.DecideAsync(r.Id, true, true);
        var identity = (await registry.StatusAsync(r.Id, r.Secret)).Identity!;
        await Reject(() => authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId)), 401);
        var challenge = await authentication.CompletionChallengeAsync(r.Id, r.Secret);
        await Reject(() => authentication.CompleteAsync(r.Id, new(r.Code, challenge.Id, Sign(key, challenge, DeviceProtocol.Complete))), 404);
        var completed = await authentication.CompleteAsync(r.Id, new(r.Secret, challenge.Id, Sign(key, challenge, DeviceProtocol.Complete)));
        Assert.Equal(identity, completed);
        await Reject(() => authentication.CompleteAsync(r.Id, new(r.Secret, challenge.Id, Sign(key, challenge, DeviceProtocol.Complete))), 409);
        await Reject(() => registry.DecideAsync(r.Id, false, false), 409);
        Assert.True((await registry.StatusAsync(r.Id, r.Secret)).CredentialsClaimed);
        var issued = await Login(identity, key);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "principal_type").Value);
        Assert.Equal(identity.CredentialId.ToString(), jwt.Claims.Single(c => c.Type == "device_credential_id").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("role", StringComparison.OrdinalIgnoreCase));
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalMinutes, 14.99, 15.01);
        Assert.True(await repository.IsAuthorizedAsync(identity.ComputerUnitId, identity.Generation, identity.CredentialId, default));
    }

    [RegistryDatabaseFact]
    public async Task AssignmentUniquenessAndConcurrentRegistrationHaveOneWinner()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var key = Key();
            try { return await Register(key); } catch (RegistryException) { return null; }
        }));
        var r = Assert.Single(attempts, x => x is not null)!;
        using var other = Key();
        await Register(other, 2); // Same number in another lab is allowed.
        var decisions = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        { try { await registry.DecideAsync(r.Id, true, true); return true; } catch (RegistryException) { return false; } }));
        Assert.Single(decisions, x => x);
        using var duplicate = Key();
        await Reject(() => Register(duplicate), 409);
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await Assert.ThrowsAsync<MySqlException>(() => Sql(c, $"INSERT INTO computer_units(computer_unit_id,unit_name,unit_number,laboratory_id) VALUES('{Guid.NewGuid()}','duplicate',1,1)"));
    }

    [RegistryDatabaseFact]
    public async Task ExpirationAndRejectionReleaseAssignmentButTimelyApprovalSurvivesDeadline()
    {
        using var first = Key(); var expired = await Register(first);
        await Execute("UPDATE unit_registrations SET expires_at=created_at WHERE registration_id=@Id", expired.Id);
        Assert.Equal("Expired", (await registry.StatusAsync(expired.Id, expired.Secret)).Status);
        await Reject(() => registry.DecideAsync(expired.Id, true, true), 409);
        using var second = Key(); var rejected = await Register(second);
        await registry.DecideAsync(rejected.Id, false, false);
        await Reject(() => registry.DecideAsync(rejected.Id, true, true), 409);
        using var third = Key(); var approved = await Register(third);
        await registry.DecideAsync(approved.Id, true, true);
        await Execute("UPDATE unit_registrations SET expires_at=created_at WHERE registration_id=@Id", approved.Id);
        await Complete(approved, third);
        Assert.Single((await registry.RegistrationsAsync("Expired")).Items);
        Assert.Single((await registry.RegistrationsAsync("Rejected")).Items);
    }

    [RegistryDatabaseFact]
    public async Task BadSignaturesAreConsumedAndChallengesRejectExpiryReplayAndWrongDevice()
    {
        using var key = Key(); var identity = await Enroll(key);
        var challenge = await authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId));
        using var wrong = Key();
        await Reject(() => authentication.TokenAsync(Proof(identity, challenge, wrong)), 401);
        await Reject(() => authentication.TokenAsync(Proof(identity, challenge, key)), 401); // Failure committed consumption.
        var expired = await authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId));
        await Execute("UPDATE device_auth_challenges SET expires_at=created_at WHERE challenge_id=@Id", expired.Id);
        await Reject(() => authentication.TokenAsync(Proof(identity, expired, key)), 401);
        using var other = Key(); var otherIdentity = await Enroll(other, 2);
        var fresh = await authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId));
        await Reject(() => authentication.TokenAsync(new(otherIdentity.ComputerUnitId, otherIdentity.CredentialId, fresh.Id, Sign(key, fresh, DeviceProtocol.Authenticate))), 401);
        await authentication.TokenAsync(Proof(identity, fresh, key));
        await Reject(() => authentication.TokenAsync(Proof(identity, fresh, key)), 401);
    }

    [RegistryDatabaseFact]
    public async Task ConcurrentChallengeConsumptionIssuesOneTokenAcrossRepositoryInstances()
    {
        using var key = Key(); var identity = await Enroll(key);
        var challenge = await authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId));
        var proof = Proof(identity, challenge, key);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            var another = new DeviceAuthenticationService(new UnitRegistryRepository(factory), registry, proofs, tokens);
            try { return await another.TokenAsync(proof); } catch (RegistryException) { return null; }
        }));
        Assert.Single(results, x => x is not null);
    }

    [RegistryDatabaseFact]
    public async Task InactivityDoesNotExpireKeysAndOutstandingChallengesAreBounded()
    {
        using var key = Key(); var identity = await Enroll(key);
        await Execute("UPDATE device_credentials SET created_at=created_at-INTERVAL 9 MONTH WHERE credential_id=@Id", identity.CredentialId);
        await Login(identity, key);
        for (var i = 0; i < 5; i++) await authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId));
        await Reject(() => authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId)), 429);
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await Sql(c, "UPDATE device_auth_challenges SET expires_at=UTC_TIMESTAMP()-INTERVAL 2 DAY");
        await repository.CleanupChallengesAsync(default);
        await Login(identity, key);
    }

    [RegistryDatabaseFact]
    public async Task DeleteDisableAndReactivationRevokeOldKeysWithoutLosingHistory()
    {
        using var oldKey = Key(); var old = await Enroll(oldKey);
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await Sql(c, $"INSERT INTO unit_history VALUES(1,'{old.ComputerUnitId}')");
        var oldChallenge = await authentication.ChallengeAsync(new(old.ComputerUnitId, old.CredentialId));
        await registry.DeleteAsync(old.ComputerUnitId);
        Assert.Empty((await registry.UnitsAsync(null, null)).Items);
        await Reject(() => authentication.TokenAsync(Proof(old, oldChallenge, oldKey)), 401);
        await Reject(() => authentication.ChallengeAsync(new(old.ComputerUnitId, old.CredentialId)), 401);
        Assert.False(await repository.IsAuthorizedAsync(old.ComputerUnitId, old.Generation, old.CredentialId, default));
        using var newKey = Key(); var next = await Enroll(newKey);
        Assert.Equal(old.ComputerUnitId, next.ComputerUnitId);
        Assert.True(next.Generation > old.Generation);
        Assert.NotEqual(old.CredentialId, next.CredentialId);
        Assert.Equal(1L, Convert.ToInt64(await new MySqlCommand("SELECT COUNT(*) FROM computer_units", c).ExecuteScalarAsync()));
        Assert.Equal(1L, Convert.ToInt64(await new MySqlCommand("SELECT COUNT(*) FROM unit_history", c).ExecuteScalarAsync()));
        await Reject(() => authentication.ChallengeAsync(new(old.ComputerUnitId, old.CredentialId)), 401);
        await registry.DisableAsync(next.ComputerUnitId);
        Assert.False(await repository.IsAuthorizedAsync(next.ComputerUnitId, next.Generation, next.CredentialId, default));
        await Reject(() => Login(next, newKey), 401);
    }

    [RegistryDatabaseFact]
    public async Task RotationRequiresBothKeysAndAtomicallyInvalidatesOldJwt()
    {
        using var oldKey = Key(); var old = await Enroll(oldKey);
        using var nextKey = Key();
        var operation = Guid.NewGuid();
        var request = new RotationChallengeRequest(operation, Public(nextKey));
        var invalid = await authentication.RotationChallengeAsync(old.ComputerUnitId, old.CredentialId, request);
        await Reject(() => authentication.RotateAsync(new(old.ComputerUnitId, old.CredentialId, invalid.Id,
            Sign(oldKey, invalid, DeviceProtocol.RotateCurrent), Sign(oldKey, invalid, DeviceProtocol.RotateReplacement))), 401);
        Assert.True(await repository.IsAuthorizedAsync(old.ComputerUnitId, old.Generation, old.CredentialId, default));
        // Retrying the same immutable proposed key recovers a lost challenge response.
        var challenge = await authentication.RotationChallengeAsync(old.ComputerUnitId, old.CredentialId, request);
        var proof = new RotationProofRequest(old.ComputerUnitId, old.CredentialId, challenge.Id,
            Sign(oldKey, challenge, DeviceProtocol.RotateCurrent), Sign(nextKey, challenge, DeviceProtocol.RotateReplacement));
        var next = await authentication.RotateAsync(proof);
        Assert.False(await repository.IsAuthorizedAsync(old.ComputerUnitId, old.Generation, old.CredentialId, default));
        Assert.True(await repository.IsAuthorizedAsync(next.ComputerUnitId, next.Generation, next.CredentialId, default));
        await Reject(() => authentication.RotateAsync(proof), 401);
        await Reject(() => Login(old, oldKey), 401);
        await Login(next, nextKey);
    }

    [RegistryDatabaseFact]
    public async Task FailedApprovalRollsBackActivationAndSignatureFailureCommitsConsumption()
    {
        using var key = Key(); var r = await Register(key);
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await Sql(c, "CREATE TRIGGER registry_test_fail_approval BEFORE UPDATE ON unit_registrations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Simulated save failure'");
        await Assert.ThrowsAsync<MySqlException>(() => registry.DecideAsync(r.Id, true, true));
        Assert.Empty((await registry.UnitsAsync(null, null)).Items);
        Assert.Equal("Pending", (await registry.StatusAsync(r.Id, r.Secret)).Status);
        await Sql(c, "DROP TRIGGER registry_test_fail_approval");
        await registry.DecideAsync(r.Id, true, true);
        await Complete(r, key);
    }

    [RegistryDatabaseFact]
    public async Task MigrationPreservesHistoricalRegistrationsAndHumanTokens()
    {
        // Reconstruct the previous registry schema from the updated fixture, then apply the actual upgrade SQL.
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await Sql(c, "ALTER TABLE unit_registrations DROP FOREIGN KEY fk_registration_credential, DROP INDEX uq_registration_key, DROP COLUMN device_credential_id, DROP COLUMN key_fingerprint, DROP COLUMN public_key; DROP TABLE device_auth_challenges; DROP TABLE device_credentials;");
        await Sql(c, "CREATE TABLE refresh_tokens(id INT PRIMARY KEY,token_hash VARCHAR(64)); INSERT INTO refresh_tokens VALUES(1,'human-history');");
        var unit = Guid.NewGuid();
        await Sql(c, $"INSERT INTO computer_units(computer_unit_id,unit_name,unit_number,laboratory_id) VALUES('{unit}','PC',1,1); INSERT INTO agent_refresh_credentials(credential_id,computer_unit_id,credential_generation,family_id,token_hash,created_at,expires_at) VALUES('{Guid.NewGuid()}','{unit}',1,'{Guid.NewGuid()}','{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}',UTC_TIMESTAMP(),UTC_TIMESTAMP()+INTERVAL 1 DAY);");
        await Script(c, Path.Combine(root, "Database/migrations/2026-11-08-persistent-device-authentication.sql"));
        Assert.Equal("human-history", await new MySqlCommand("SELECT token_hash FROM refresh_tokens", c).ExecuteScalarAsync());
        Assert.Equal(0L, Convert.ToInt64(await new MySqlCommand("SELECT COUNT(*) FROM agent_refresh_credentials WHERE revoked_at IS NULL", c).ExecuteScalarAsync()));
        Assert.Equal(1L, Convert.ToInt64(await new MySqlCommand("SELECT COUNT(*) FROM computer_units", c).ExecuteScalarAsync()));
    }

    private Task<RegistrationReceipt> Register(ECDsa key, int lab = 1) => registry.RegisterAsync(new(lab, 1, "test-workstation", "Windows test", "1.0", Public(key)));
    private async Task<DeviceIdentity> Enroll(ECDsa key, int lab = 1)
    { var r = await Register(key, lab); await registry.DecideAsync(r.Id, true, true); return await Complete(r, key); }
    private async Task<DeviceIdentity> Complete(RegistrationReceipt r, ECDsa key)
    { var c = await authentication.CompletionChallengeAsync(r.Id, r.Secret); return await authentication.CompleteAsync(r.Id, new(r.Secret, c.Id, Sign(key, c, DeviceProtocol.Complete))); }
    private async Task<AgentTokens> Login(DeviceIdentity identity, ECDsa key)
    { var c = await authentication.ChallengeAsync(new(identity.ComputerUnitId, identity.CredentialId)); return await authentication.TokenAsync(Proof(identity, c, key)); }
    private static DeviceProofRequest Proof(DeviceIdentity identity, DeviceChallengeResponse c, ECDsa key) => new(identity.ComputerUnitId, identity.CredentialId, c.Id, Sign(key, c, DeviceProtocol.Authenticate));
    private static ECDsa Key() => ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static string Public(ECDsa key) => DeviceProtocol.Encode(key.ExportSubjectPublicKeyInfo());
    private static string Sign(ECDsa key, DeviceChallengeResponse c, string purpose) => DeviceProtocol.Encode(key.SignData(
        DeviceProtocol.Payload(purpose, c.Audience, c.ComputerUnitId, c.CredentialId, c.ContextId, c.Id, c.Nonce, c.ReplacementFingerprint),
        HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    private static async Task Reject(Func<Task> action, int status) => Assert.Equal(status, (await Assert.ThrowsAsync<RegistryException>(action)).StatusCode);
    private async Task Execute(string sql, Guid id)
    { await using var c = factory.CreateConnection(); await c.OpenAsync(); await using var command = new MySqlCommand(sql, c); command.Parameters.AddWithValue("@Id", id.ToString()); await command.ExecuteNonQueryAsync(); }
    private static async Task Script(MySqlConnection c, string file)
    {
        var sql = string.Join('\n', (await File.ReadAllLinesAsync(file)).Where(x => !x.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) await Sql(c, statement);
    }
    private static async Task Sql(MySqlConnection c, string sql)
    { await using var command = new MySqlCommand(sql, c); await command.ExecuteNonQueryAsync(); }
    public async Task DisposeAsync()
    {
        if (admin is null) return;
        if (database.StartsWith("clms_registry_test_", StringComparison.Ordinal) && database.Length == 51)
            await Sql(admin, $"DROP DATABASE `{database}`");
        await admin.DisposeAsync();
    }
    private sealed class AdminActor : ICurrentUser { public Task<Guid> RequirePrivilegeAsync(string privilege) => RequireAdminAsync(); public Task<Guid> RequireAdminAsync() => Task.FromResult(Administrator); }
}

public sealed class RegistryDatabaseFactAttribute : FactAttribute
{
    public RegistryDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CLMS_REGISTRY_INTEGRATION") != "1" &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL")))
            Skip = "Set CLMS_REGISTRY_INTEGRATION=1 or CLMS_TEST_MYSQL to run disposable MySQL integration tests.";
    }
}