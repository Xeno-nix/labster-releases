using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;
using CLMS.Shared.Security;

namespace CLMS.Application.UnitRegistry;

/// <summary>Coordinates persistent device proofs using the registry's existing transactional boundary.</summary>
public sealed class DeviceAuthenticationService(IUnitRegistryRepository repository, UnitRegistryService registry,
    IDeviceProofVerifier proofs, IAgentTokenService tokens)
{
    /// <summary>Issues a short-lived challenge only for an active registered identity.</summary>
    public Task<DeviceChallengeResponse> ChallengeAsync(DeviceChallengeRequest request, CancellationToken ct = default) =>
        repository.TransactionAsync(new(RegistryLockKind.Unit, request.DeviceId.ToString()), async tx =>
        {
            var (_, credential) = await RequireCredential(tx, request.DeviceId, request.CredentialId);
            return await CreateChallenge(tx, credential, DeviceProtocol.Authenticate, Guid.Empty);
        }, ct);

    /// <summary>Consumes a proof once, commits failures, and issues a JWT only after a verified signature.</summary>
    public async Task<AgentTokens> TokenAsync(DeviceProofRequest request, CancellationToken ct = default)
    {
        var result = await repository.TransactionAsync<AgentTokens?>(new(RegistryLockKind.Unit, request.DeviceId.ToString()), async tx =>
        {
            var (unit, credential) = await RequireCredential(tx, request.DeviceId, request.CredentialId);
            var challenge = await Consume(tx, request.ChallengeId, credential, DeviceProtocol.Authenticate);
            if (challenge is null || !Verify(credential, challenge, request.Signature)) return null;
            await tx.VerifyChallengeAsync(challenge.Id);
            return tokens.Issue(unit.Id, unit.CredentialGeneration, credential.Id, tx.Now);
        }, ct);
        return result ?? throw RegistryException.InvalidCredential();
    }

    /// <summary>Authenticates the private registration receipt before exposing a completion challenge.</summary>
    public Task<DeviceChallengeResponse> CompletionChallengeAsync(Guid registrationId, string secret, CancellationToken ct = default) =>
        repository.TransactionAsync(new(RegistryLockKind.Registration, registrationId.ToString()), async tx =>
        {
            var registration = await registry.AuthenticateRegistration(tx, registrationId, secret);
            var credential = await PendingCompletion(tx, registration);
            return await CreateChallenge(tx, credential, DeviceProtocol.Complete, registration.Id);
        }, ct);

    /// <summary>Activates the approved key once. Lost responses recover through status plus ordinary authentication.</summary>
    public async Task<DeviceIdentity> CompleteAsync(Guid registrationId, ClaimUnitRequest request, CancellationToken ct = default)
    {
        var result = await repository.TransactionAsync<DeviceIdentity?>(new(RegistryLockKind.Registration, registrationId.ToString()), async tx =>
        {
            var registration = await registry.AuthenticateRegistration(tx, registrationId, request.Secret);
            var credential = await PendingCompletion(tx, registration);
            var challenge = await Consume(tx, request.ChallengeId, credential, DeviceProtocol.Complete);
            if (challenge is null || challenge.ContextId != registrationId || !Verify(credential, challenge, request.Signature)) return null;
            await tx.VerifyChallengeAsync(challenge.Id);
            credential.ActivatedAtUtc = tx.Now;
            registration.ClaimedAtUtc = tx.Now;
            await tx.SaveCredentialAsync(credential);
            await tx.SaveRegistrationAsync(registration);
            return Identity(credential);
        }, ct);
        return result ?? throw RegistryException.InvalidCredential();
    }

    /// <summary>Registers an immutable replacement key and binds a challenge to both credentials and the durable operation ID.</summary>
    public Task<DeviceChallengeResponse> RotationChallengeAsync(Guid deviceId, Guid credentialId, RotationChallengeRequest request, CancellationToken ct = default)
    {
        var key = proofs.ValidatePublicKey(request.PublicKey);
        if (request.OperationId == Guid.Empty) throw new RegistryException("invalid_rotation", "An operation identifier is required.", 400);
        return repository.TransactionAsync(new(RegistryLockKind.Unit, deviceId.ToString()), async tx =>
        {
            var (_, current) = await RequireCredential(tx, deviceId, credentialId);
            var replacement = await tx.CredentialByFingerprintAsync(key.Fingerprint);
            if (replacement is not null && (replacement.ComputerUnitId != deviceId || replacement.ReplacesId != current.Id ||
                replacement.ActivatedAtUtc.HasValue || replacement.RevokedAtUtc.HasValue)) throw RegistryException.InvalidCredential();
            if (replacement is null)
            {
                replacement = new DeviceCredential
                {
                    Id = Guid.NewGuid(), ComputerUnitId = deviceId, Generation = current.Generation,
                    PublicKey = key.PublicKey, Fingerprint = key.Fingerprint, CreatedAtUtc = tx.Now, ReplacesId = current.Id
                };
                await tx.InsertCredentialAsync(replacement);
            }
            return await CreateChallenge(tx, current, DeviceProtocol.RotateCurrent, request.OperationId, replacement);
        }, ct);
    }

    /// <summary>Atomically revokes the current credential and activates its replacement after both possession proofs succeed.</summary>
    public async Task<DeviceIdentity> RotateAsync(RotationProofRequest request, CancellationToken ct = default)
    {
        var result = await repository.TransactionAsync<DeviceIdentity?>(new(RegistryLockKind.Unit, request.DeviceId.ToString()), async tx =>
        {
            var (_, current) = await RequireCredential(tx, request.DeviceId, request.CredentialId);
            var challenge = await Consume(tx, request.ChallengeId, current, DeviceProtocol.RotateCurrent);
            var next = challenge?.ReplacementCredentialId is { } id ? await tx.CredentialAsync(id) : null;
            if (challenge is null || next is null || next.ReplacesId != current.Id || next.RevokedAtUtc.HasValue ||
                next.ActivatedAtUtc.HasValue || next.ComputerUnitId != current.ComputerUnitId || next.Generation != current.Generation ||
                !Verify(current, challenge, request.Signature, next.Fingerprint) ||
                !proofs.Verify(next.PublicKey, Payload(challenge, DeviceProtocol.RotateReplacement, next.Fingerprint), request.ReplacementSignature)) return null;
            await tx.VerifyChallengeAsync(challenge.Id);
            current.RevokedAtUtc = tx.Now;
            current.ReplacedById = next.Id;
            await tx.SaveCredentialAsync(current); // Release unique active-unit constraint first.
            next.ActivatedAtUtc = tx.Now;
            await tx.SaveCredentialAsync(next);
            return Identity(next);
        }, ct);
        return result ?? throw RegistryException.InvalidCredential();
    }

    private DeviceIdentity Identity(DeviceCredential credential) => new(credential.ComputerUnitId, credential.Id, credential.Generation, tokens.Audience);

    private async Task<DeviceCredential> PendingCompletion(IUnitRegistryTransaction tx, UnitRegistration registration)
    {
        if (registration.Status != RegistrationStatus.Approved || registration.ClaimedAtUtc.HasValue ||
            registration.DeviceCredentialId is not { } credentialId || registration.ComputerUnitId is not { } unitId)
            throw new RegistryException("claim_unavailable", "Registration completion is unavailable.");
        var (_, credential) = await RequireCredential(tx, unitId, credentialId, false);
        if (credential.ActivatedAtUtc.HasValue) throw RegistryException.InvalidCredential();
        return credential;
    }

    private static async Task<(ComputerUnit Unit, DeviceCredential Credential)> RequireCredential(IUnitRegistryTransaction tx,
        Guid deviceId, Guid credentialId, bool requireActive = true)
    {
        var unit = await tx.UnitAsync(deviceId);
        var credential = await tx.CredentialAsync(credentialId);
        if (unit is null || !unit.Enabled || unit.DeletedAt.HasValue || credential is null ||
            credential.ComputerUnitId != unit.Id || credential.Generation != unit.CredentialGeneration ||
            credential.RevokedAtUtc.HasValue || requireActive && !credential.IsActive) throw RegistryException.InvalidCredential();
        return (unit, credential);
    }

    private async Task<DeviceChallengeResponse> CreateChallenge(IUnitRegistryTransaction tx, DeviceCredential credential,
        string purpose, Guid context, DeviceCredential? replacement = null)
    {
        if (await tx.OutstandingChallengesAsync(credential.ComputerUnitId) >= 5)
            throw new RegistryException("rate_limited", "Try again shortly.", 429);
        var challenge = new DeviceChallenge
        {
            Id = Guid.NewGuid(), ComputerUnitId = credential.ComputerUnitId, CredentialId = credential.Id,
            Purpose = purpose, Audience = tokens.Audience, ContextId = context, Nonce = tokens.NewSecret(),
            CreatedAtUtc = tx.Now, ExpiresAtUtc = tx.Now.AddSeconds(60), ReplacementCredentialId = replacement?.Id
        };
        await tx.InsertChallengeAsync(challenge);
        return new(challenge.Id, challenge.Nonce, challenge.Audience, challenge.ExpiresAtUtc,
            challenge.ComputerUnitId, challenge.CredentialId, challenge.ContextId, replacement?.Id, replacement?.Fingerprint ?? "");
    }

    private static async Task<DeviceChallenge?> Consume(IUnitRegistryTransaction tx, Guid id, DeviceCredential credential, string purpose)
    {
        var challenge = await tx.ChallengeAsync(id);
        if (challenge is null || challenge.ComputerUnitId != credential.ComputerUnitId || challenge.CredentialId != credential.Id ||
            challenge.Purpose != purpose || !challenge.CanConsume(tx.Now)) return null;
        await tx.ConsumeChallengeAsync(challenge.Id);
        return challenge;
    }

    private bool Verify(DeviceCredential credential, DeviceChallenge challenge, string signature, string fingerprint = "") =>
        challenge.Audience == tokens.Audience && proofs.Verify(credential.PublicKey, Payload(challenge, challenge.Purpose, fingerprint), signature);

    private static byte[] Payload(DeviceChallenge challenge, string purpose, string fingerprint) =>
        DeviceProtocol.Payload(purpose, challenge.Audience, challenge.ComputerUnitId, challenge.CredentialId,
            challenge.ContextId, challenge.Id, challenge.Nonce, fingerprint);
}
