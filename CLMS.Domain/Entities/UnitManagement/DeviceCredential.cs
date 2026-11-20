namespace CLMS.Domain.Entities.UnitManagement;

/// <summary>A persistent public device identity; authorization ends through explicit revocation, never inactivity.</summary>
public sealed class DeviceCredential
{
    public Guid Id { get; init; }
    public Guid ComputerUnitId { get; init; }
    public int Generation { get; init; }
    public string PublicKey { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string Algorithm { get; init; } = "ES256";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedById { get; set; }
    public Guid? ReplacesId { get; init; }
    public bool IsActive => ActivatedAtUtc.HasValue && !RevokedAtUtc.HasValue;
}

/// <summary>A server-owned, purpose-bound, single-use proof challenge.</summary>
public sealed class DeviceChallenge
{
    public Guid Id { get; init; }
    public Guid ComputerUnitId { get; init; }
    public Guid CredentialId { get; init; }
    public Guid ContextId { get; init; }
    public Guid? ReplacementCredentialId { get; init; }
    public string Purpose { get; init; } = "";
    public string Audience { get; init; } = "";
    public string Nonce { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; init; }
    public bool CanConsume(DateTime now) => !ConsumedAtUtc.HasValue && now < ExpiresAtUtc;
}
