namespace CLMS.Shared.Dtos;

public sealed record RegistrationLaboratory(int Id, string Name);
public sealed record RegisterUnitRequest(int LaboratoryId, int UnitNumber, string Hostname,
    string OperatingSystem, string AgentVersion, string PublicKey);
public sealed record RegistrationReceipt(Guid Id, string Code, string Secret, DateTime ServerTimeUtc,
    DateTime ExpiresAtUtc);
public sealed record RegistrationSecret(string Secret);
public sealed record ClaimUnitRequest(string Secret, Guid ChallengeId, string Signature);
public sealed record RegistrationState(string Status, DateTime ServerTimeUtc, DateTime ExpiresAtUtc,
    bool CredentialsClaimed, DeviceIdentity? Identity = null);
public sealed record DeviceIdentity(Guid ComputerUnitId, Guid CredentialId, int Generation, string Audience);
public sealed record AgentTokens(string AccessToken, DateTime AccessExpiresAtUtc, Guid ComputerUnitId,
    int Generation, Guid CredentialId);
public sealed record DeviceChallengeRequest(Guid DeviceId, Guid CredentialId);
public sealed record DeviceProofRequest(Guid DeviceId, Guid CredentialId, Guid ChallengeId, string Signature);
public sealed record RotationChallengeRequest(Guid OperationId, string PublicKey);
public sealed record RotationProofRequest(Guid DeviceId, Guid CredentialId, Guid ChallengeId,
    string Signature, string ReplacementSignature);
public sealed record DeviceChallengeResponse(Guid Id, string Nonce, string Audience, DateTime ExpiresAtUtc,
    Guid ComputerUnitId, Guid CredentialId, Guid ContextId, Guid? ReplacementCredentialId = null,
    string ReplacementFingerprint = "");
public sealed record RegistryUnit(Guid Id, int LaboratoryId, string Laboratory, byte UnitNumber,
    string UnitName, string? Hostname, string? OperatingSystem, string? AgentVersion, bool Enabled,
    DateTime? RegisteredAtUtc, DateTime? LastSeenUtc, bool Online)
{
    // MySQL boolean expressions are Int32; let Dapper perform property conversion.
    public RegistryUnit() : this(Guid.Empty, 0, "", 0, "", null, null, null, false, null, null, false) { }
}
public sealed record RegistryRegistration(Guid Id, int LaboratoryId, string Laboratory, byte UnitNumber,
    string Code, string Hostname, string OperatingSystem, string AgentVersion, string Status,
    DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? DecidedAtUtc, string? KeyFingerprint = null);
public sealed record RegistryPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record RegistrationDecision(bool PhysicalCodeVerified);
