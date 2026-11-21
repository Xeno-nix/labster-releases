using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;

namespace CLMS.Application.UnitRegistry;

public sealed class UnitRegistryService(IUnitRegistryRepository repository, ILaboratoryRepository laboratories,
    IRefreshTokenHasher hasher, IAgentTokenService tokens, ICurrentUser actor, IDeviceProofVerifier proofs)
{
    public async Task<IReadOnlyList<RegistrationLaboratory>> LaboratoriesAsync() =>
        (await laboratories.GetAllAsync()).Select(x => new RegistrationLaboratory(x.Id, x.Name)).ToList();

    public Task<RegistrationReceipt> RegisterAsync(RegisterUnitRequest request, CancellationToken ct = default)
    {
        if (request.LaboratoryId <= 0 || request.UnitNumber is < 1 or > 255 ||
            !ValidText(request.Hostname, 255) || !ValidText(request.OperatingSystem, 255) || !ValidText(request.AgentVersion, 64))
            throw new RegistryException("invalid_registration", "Select a laboratory, enter unit number 1–255, and provide valid device metadata.", 400);
        var key = proofs.ValidatePublicKey(request.PublicKey);
        return repository.TransactionAsync(new(RegistryLockKind.Laboratory, request.LaboratoryId.ToString()), async tx =>
        {
            await tx.ExpirePendingAsync(request.LaboratoryId);
            var unit = await tx.AssignmentAsync(request.LaboratoryId, (byte)request.UnitNumber);
            if (unit is { DeletedAt: null }) throw new RegistryException("assignment_occupied", "This unit assignment is already registered.");
            if (await tx.HasPendingAsync(request.LaboratoryId, (byte)request.UnitNumber))
                throw new RegistryException("registration_pending", "An unexpired request already exists for this assignment.");
            var secret = tokens.NewSecret();
            var registration = new UnitRegistration
            {
                Id = Guid.NewGuid(), LaboratoryId = request.LaboratoryId, UnitNumber = (byte)request.UnitNumber,
                Code = tokens.NewCode(), SecretHash = hasher.Hash(secret), PublicKey = key.PublicKey, KeyFingerprint = key.Fingerprint, Hostname = request.Hostname.Trim(),
                OperatingSystem = request.OperatingSystem.Trim(), AgentVersion = request.AgentVersion.Trim(),
                CreatedAtUtc = tx.Now, ExpiresAtUtc = tx.Now.AddMinutes(10)
            };
            await tx.InsertRegistrationAsync(registration);
            return new RegistrationReceipt(registration.Id, registration.Code, secret, tx.Now, registration.ExpiresAtUtc);
        }, ct);
    }

    public Task<RegistrationState> StatusAsync(Guid id, string secret, CancellationToken ct = default) =>
        repository.TransactionAsync(new(RegistryLockKind.Registration, id.ToString()), async tx =>
        {
            var registration = await AuthenticateRegistration(tx, id, secret);
            if (registration.Expire(tx.Now)) await tx.SaveRegistrationAsync(registration);
            return new RegistrationState(registration.Status.ToString(), tx.Now, registration.ExpiresAtUtc, registration.ClaimedAtUtc.HasValue,
                registration.ComputerUnitId.HasValue && registration.DeviceCredentialId.HasValue
                    ? new DeviceIdentity(registration.ComputerUnitId.Value, registration.DeviceCredentialId.Value, registration.CredentialGeneration!.Value, tokens.Audience) : null);
        }, ct);

    public async Task DecideAsync(Guid id, bool approve, bool physicalCodeVerified, CancellationToken ct = default)
    {
        var administrator = await actor.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.UnitsManage);
        if (approve && !physicalCodeVerified) throw new RegistryException("verify_code", "Verify the code on the physical computer before approving.", 400);
        // Return terminal conflicts after commit so expiration is persisted.
        var conflict = await repository.TransactionAsync<RegistryException?>(new(RegistryLockKind.Registration, id.ToString()), async tx =>
        {
            var registration = await tx.RegistrationAsync(id) ?? throw RegistryException.Unavailable();
            if (registration.Expire(tx.Now)) await tx.SaveRegistrationAsync(registration);
            if (registration.Status != RegistrationStatus.Pending)
                return new RegistryException("registration_completed", "This request is no longer pending.");
            if (approve)
            {
                var unit = await tx.AssignmentAsync(registration.LaboratoryId, registration.UnitNumber);
                if (unit is { DeletedAt: null }) return new RegistryException("assignment_occupied", "This assignment is already occupied.");
                var create = unit is null;
                unit ??= new ComputerUnit(Guid.NewGuid(), $"PC-{registration.UnitNumber:000}", registration.UnitNumber, registration.LaboratoryId)
                    { CreatedAt = tx.Now };
                unit.DeletedAt = null;
                unit.Enabled = true;
                unit.CredentialGeneration++;
                unit.Hostname = registration.Hostname;
                unit.OperatingSystem = registration.OperatingSystem;
                unit.AgentVersion = registration.AgentVersion;
                unit.RegisteredAtUtc = unit.LastModifiedAt = tx.Now;
                await tx.SaveUnitAsync(unit, create);
                await tx.RevokeCredentialsAsync(unit.Id);
                await tx.SetOfflineAsync(unit.Id);
                var credential = new DeviceCredential
                {
                    Id = Guid.NewGuid(), ComputerUnitId = unit.Id, Generation = unit.CredentialGeneration,
                    PublicKey = registration.PublicKey, Fingerprint = registration.KeyFingerprint, CreatedAtUtc = tx.Now
                };
                await tx.InsertCredentialAsync(credential);
                registration.DeviceCredentialId = credential.Id;
                registration.ComputerUnitId = unit.Id;
                registration.CredentialGeneration = unit.CredentialGeneration;
            }
            registration.Decide(approve, administrator, tx.Now);
            await tx.SaveRegistrationAsync(registration);
            return null;
        }, ct);
        if (conflict is not null) throw conflict;
    }

    public async Task<RegistryPage<RegistryUnit>> UnitsAsync(int? lab, string? search, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        await actor.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.UnitsManage);
        return await repository.UnitsAsync(lab, search, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), ct);
    }

    public async Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status = "Pending", int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        await actor.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.UnitsManage);
        if (status is not null && !Enum.TryParse<RegistrationStatus>(status, false, out _))
            throw new RegistryException("invalid_status", "Unknown registration status.", 400);
        return await repository.RegistrationsAsync(status, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await DeactivateAsync(id, true, ct);

    public async Task DisableAsync(Guid id, CancellationToken ct = default)
        => await DeactivateAsync(id, false, ct);

    private async Task DeactivateAsync(Guid id, bool delete, CancellationToken ct)
    {
        await actor.RequirePrivilegeAsync(CLMS.Application.Authorization.Privileges.UnitsManage);
        await repository.TransactionAsync(new(RegistryLockKind.Unit, id.ToString()), async tx =>
        {
            var unit = await tx.UnitAsync(id) ?? throw new RegistryException("unit_not_found", "Unit not found.", 404);
            if (!unit.DeletedAt.HasValue && (delete || unit.Enabled))
            {
                if (delete) unit.DeletedAt = tx.Now;
                unit.LastModifiedAt = tx.Now;
                unit.Enabled = false;
                unit.CredentialGeneration++;
                await tx.SaveUnitAsync(unit, false);
            }
            await tx.RevokeCredentialsAsync(id);
            await tx.SetOfflineAsync(id);
            return true;
        }, ct);
    }

    public Task<bool> HeartbeatAsync(Guid id, int generation, Guid credentialId, string? ip, CancellationToken ct = default) =>
        repository.TransactionAsync(new(RegistryLockKind.Unit, id.ToString()), async tx =>
        {
            // Recheck in the transaction to close the authorization/deletion race.
            var unit = await tx.UnitAsync(id);
            var credential = await tx.CredentialAsync(credentialId);
            if (unit is null || !unit.Enabled || unit.DeletedAt.HasValue || unit.CredentialGeneration != generation ||
                credential is null || !credential.IsActive || credential.ComputerUnitId != id || credential.Generation != generation)
                throw RegistryException.InvalidCredential();
            await tx.HeartbeatAsync(id, ip);
            return true;
        }, ct);

    internal async Task<UnitRegistration> AuthenticateRegistration(IUnitRegistryTransaction tx, Guid id, string secret)
    {
        var registration = await tx.RegistrationAsync(id);
        if (registration is null || string.IsNullOrEmpty(secret) || secret.Length > 128 || !hasher.Verify(secret, registration.SecretHash))
            throw RegistryException.Unavailable();
        return registration;
    }

    private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && !value.Any(char.IsControl);

}
