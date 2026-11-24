using CLMS.Application.DTOs.Logbook;
using CLMS.Application.UnitRegistry;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Infrastructure.Repositories;
using Dapper;
using MySqlConnector;
using static DatabaseChecks;

internal static class DeviceAndLogChecks
{
    internal static async Task Run(MySqlConnection db, MySqlConnectionFactory factory, Guid faculty, string student)
    {
        var registry = new UnitRegistryRepository(factory);
        var registration = new UnitRegistration { Id = Guid.NewGuid(), LaboratoryId = 1, UnitNumber = 1,
            Code = "TEST1234", SecretHash = new string('S', 44), PublicKey = "test-public-key", KeyFingerprint = new string('F', 43),
            Hostname = "test-unit", OperatingSystem = "Windows", AgentVersion = "1.0",
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) };
        await registry.TransactionAsync(new(RegistryLockKind.Laboratory, "1"), async tx =>
        {
            await tx.ExpirePendingAsync(1);
            await tx.InsertRegistrationAsync(registration);
            Require(await tx.HasPendingAsync(1, 1) && (await tx.RegistrationAsync(registration.Id))?.Id == registration.Id, "registration insert and locked read");
            return true;
        });
        Require((await registry.RegistrationsAsync("Pending", 1, 10)).Total == 1, "registration paging and function filter");
        var unit = new ComputerUnit(Guid.NewGuid(), "Test PC", 1, 1);
        var credential = new DeviceCredential { Id = Guid.NewGuid(), ComputerUnitId = unit.Id, Generation = 1,
            PublicKey = "test-public-key", Fingerprint = new string('F', 43), CreatedAtUtc = DateTime.UtcNow, ActivatedAtUtc = DateTime.UtcNow };
        await registry.TransactionAsync(new(RegistryLockKind.Registration, registration.Id.ToString()), async tx =>
        {
            unit.CredentialGeneration = 1; unit.RegisteredAtUtc = tx.Now; unit.Enabled = true;
            await tx.SaveUnitAsync(unit, true);
            await tx.InsertCredentialAsync(credential);
            registration.ComputerUnitId = unit.Id; registration.CredentialGeneration = 1; registration.DeviceCredentialId = credential.Id;
            registration.Decide(true, faculty, tx.Now);
            await tx.SaveRegistrationAsync(registration);
            Require((await tx.AssignmentAsync(1, 1))?.Id == unit.Id && (await tx.UnitAsync(unit.Id))?.Id == unit.Id, "registry assignment mapping");
            Require((await tx.CredentialAsync(credential.Id))?.IsActive == true && (await tx.CredentialByFingerprintAsync(credential.Fingerprint))?.Id == credential.Id, "credential mapping");
            await tx.SaveCredentialAsync(credential);
            var challenge = new DeviceChallenge { Id = Guid.NewGuid(), ComputerUnitId = unit.Id, CredentialId = credential.Id,
                ContextId = Guid.Empty, Purpose = "authenticate", Audience = "CLMS.Agent", Nonce = new string('N', 43),
                CreatedAtUtc = tx.Now, ExpiresAtUtc = tx.Now.AddMinutes(1) };
            await tx.InsertChallengeAsync(challenge);
            Require(await tx.OutstandingChallengesAsync(unit.Id) == 1 && (await tx.ChallengeAsync(challenge.Id))?.Id == challenge.Id, "challenge creation and mapping");
            await tx.ConsumeChallengeAsync(challenge.Id); await tx.VerifyChallengeAsync(challenge.Id);
            Require(await tx.OutstandingChallengesAsync(unit.Id) == 0 && (await tx.ChallengeAsync(challenge.Id))?.VerifiedAtUtc is not null, "challenge consumption and verification");
            await tx.HeartbeatAsync(unit.Id, "127.0.0.1");
            return true;
        });
        Require(await registry.IsAuthorizedAsync(unit.Id, 1, credential.Id, default), "active device authorization");
        Require((await registry.UnitsAsync(1, "1", 1, 10)).Total == 1 && (await registry.RegistrationsAsync("Approved", 1, 10)).Total == 1, "registry multi-result pages");
        var activityBefore = await Scalar(db, "SELECT COUNT(*) FROM unit_activity");
        await registry.TransactionAsync(new(RegistryLockKind.Unit, unit.Id.ToString()), async tx => { await tx.HeartbeatAsync(unit.Id, "127.0.0.1"); return true; });
        Require(await Scalar(db, "SELECT COUNT(*) FROM unit_activity") == activityBefore, "repeated heartbeat creates no activity spam");
        try
        {
            await registry.TransactionAsync<int>(new(RegistryLockKind.Unit, unit.Id.ToString()), async tx => { await tx.SetOfflineAsync(unit.Id); throw new InvalidOperationException("rollback check"); });
        }
        catch (InvalidOperationException) { }
        Require(await Scalar(db, "SELECT is_online FROM unit_availability") == 1 && await Scalar(db, "SELECT COUNT(*) FROM unit_activity") == activityBefore, "registry rollback includes trigger writes");
        try { await registry.TransactionAsync(new(RegistryLockKind.Unit, Guid.NewGuid().ToString()), _ => Task.FromResult(true)); throw new Exception("Missing unit accepted"); }
        catch (RegistryException ex) { Require(ex.Code == "invalid_credential", "missing device error preserved"); }
        await registry.CleanupChallengesAsync(default);

        var units = new ComputerUnitRepository(factory);
        Require(await units.ExistsActiveAtLabAndNumberAsync(1, 1) && await units.ExistsAtLabAndNumberAsync(1, 1), "unit assignment existence");
        Require(!await units.ExistsAtLabAndNumberAsync(1, 1, unit.Id), "unit existence exclusion");
        Require((await units.GetAllAsync()).Count() == 1 && (await units.GetByIdAsync(unit.Id))?.Id == unit.Id, "unit inventory projections");
        await units.UpdateAsync(unit);

        var equipment = new EquipmentRepository(factory);
        var item = new Equipment(Guid.NewGuid(), "EQ-001", null, "Keyboard", null, null, "Peripheral", EquipmentStatus.Available, false, false, null);
        await equipment.CreateAsync(item); await equipment.UpdateAsync(item);
        Require(await equipment.GetNextEquipmentNoAsync() == "EQ-002" && (await equipment.GetAllAsync()).Count() == 1, "equipment identity suggestion and list");
        var assignment = new EquipmentAssignment(Guid.NewGuid(), item.Id, unit.Id, null, DateTime.UtcNow);
        await equipment.CreateAssignmentAsync(assignment);
        Require((await equipment.GetActiveAssignmentAsync(item.Id))?.Id == assignment.Id && await equipment.GetAssignmentByIdAsync(assignment.Id) is not null, "equipment active assignment");
        Require((await equipment.GetAssignmentsForComputerUnitAsync(unit.Id)).Count() == 1 && (await equipment.GetAssignmentHistoryAsync(item.Id)).Count() == 1 &&
            (await equipment.GetByIdAsync(item.Id))?.CurrentComputerUnitId == unit.Id, "equipment location DTO mapping");
        assignment.End(AssignmentEndReason.Returned, "Test", DateTime.UtcNow);
        await equipment.EndAssignmentAsync(assignment);
        Require((await equipment.GetAssignmentByIdAsync(assignment.Id))?.Status == AssignmentStatus.Ended, "ended assignment state restored");
        await equipment.DeleteAsync(item.Id);

        var guests = new GuestRepository(factory);
        var guest = new Guest("Test visitor", "Testing");
        var guestId = await guests.CreateAsync(guest);
        Require((await guests.GetByIdAsync(guestId))?.GuestName == guest.GuestName, "guest scalar ID and mapping");
        await LogChecks.Run(db, factory, faculty, student, unit.Id, guestId);

        var casts = new ScreenCastRepository(factory);
        var session = await casts.CreateAsync(faculty, [unit.Id], DateTime.UtcNow);
        await casts.SetConnectionAsync(session, unit.Id, true, DateTime.UtcNow);
        Require(await Scalar(db, "SELECT COUNT(*) FROM screen_cast_connections WHERE status='connected'") == 1, "cast create and connect");
        await casts.EndAsync(session, false, DateTime.UtcNow);
        Require(await Scalar(db, "SELECT COUNT(*) FROM screen_cast_sessions WHERE status='ended'") == 1, "cast atomic end");
        await casts.CreateAsync(faculty, [unit.Id], DateTime.UtcNow);
        await casts.RecoverInterruptedAsync(DateTime.UtcNow);
        Require(await Scalar(db, "SELECT COUNT(*) FROM screen_cast_sessions WHERE status='active'") == 0, "cast recovery closes active sessions");
        var countBefore = await Scalar(db, "SELECT COUNT(*) FROM screen_cast_sessions");
        try { await casts.CreateAsync(faculty, [Guid.NewGuid()], DateTime.UtcNow); throw new Exception("Invalid cast unit accepted"); }
        catch (MySqlException ex) when (ex.Number == 1452) { }
        Require(await Scalar(db, "SELECT COUNT(*) FROM screen_cast_sessions") == countBefore, "cast failure rolls back session");
        await units.DeleteAsync(unit.Id);
        Require(!await registry.IsAuthorizedAsync(unit.Id, 1, credential.Id, default), "unit deletion immediately revokes authorization");
    }
}
