namespace CLMS.Domain.Entities.UnitManagement;

public enum RegistrationStatus { Pending, Approved, Rejected, Expired }

public sealed class UnitRegistration
{
    public Guid Id { get; init; }
    public int LaboratoryId { get; init; }
    public byte UnitNumber { get; init; }
    public string Code { get; init; } = "";
    public string SecretHash { get; init; } = "";
    public string PublicKey { get; init; } = "";
    public string KeyFingerprint { get; init; } = "";
    public Guid? DeviceCredentialId { get; set; }
    public string Hostname { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string AgentVersion { get; init; } = "";
    public RegistrationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? DecidedAtUtc { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public Guid? ComputerUnitId { get; set; }
    public int? CredentialGeneration { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }

    public bool Expire(DateTime now)
    {
        if (Status != RegistrationStatus.Pending || now < ExpiresAtUtc) return false;
        Status = RegistrationStatus.Expired;
        return true;
    }

    public void Decide(bool approve, Guid administrator, DateTime now)
    {
        Expire(now);
        if (Status != RegistrationStatus.Pending)
            throw new InvalidOperationException("Only an unexpired pending request can be processed.");
        Status = approve ? RegistrationStatus.Approved : RegistrationStatus.Rejected;
        DecidedBy = administrator;
        DecidedAtUtc = now;
    }

    public static UnitRegistration Restore(UnitRegistration registration, RegistrationStatus status,
        Guid? decidedBy, DateTime? decidedAt)
    {
        registration.Status = status;
        registration.DecidedBy = decidedBy;
        registration.DecidedAtUtc = decidedAt;
        return registration;
    }
}
