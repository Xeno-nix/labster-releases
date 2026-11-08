using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Shared.Dtos;

namespace CLMS.Application.UnitRegistry;

public enum RegistryLockKind { Laboratory, Registration, Unit }
public sealed record RegistryLock(RegistryLockKind Kind, string Key);

public interface IUnitRegistryRepository
{
    Task<T> TransactionAsync<T>(RegistryLock key, Func<IUnitRegistryTransaction, Task<T>> action,
        CancellationToken cancellationToken = default);
    Task<RegistryPage<RegistryUnit>> UnitsAsync(int? laboratoryId, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task CleanupChallengesAsync(CancellationToken cancellationToken);
    Task<bool> IsAuthorizedAsync(Guid unitId, int generation, Guid credentialId, CancellationToken cancellationToken);
}

// Every operation shares the connection and transaction whose laboratory row is locked.
public interface IUnitRegistryTransaction
{
    DateTime Now { get; }
    Task ExpirePendingAsync(int laboratoryId);
    Task<bool> HasPendingAsync(int laboratoryId, byte unitNumber);
    Task<UnitRegistration?> RegistrationAsync(Guid id);
    Task InsertRegistrationAsync(UnitRegistration registration);
    Task SaveRegistrationAsync(UnitRegistration registration);
    Task<ComputerUnit?> AssignmentAsync(int laboratoryId, byte number);
    Task<ComputerUnit?> UnitAsync(Guid id);
    Task SaveUnitAsync(ComputerUnit unit, bool create);
    Task<DeviceCredential?> CredentialAsync(Guid id);
    Task InsertCredentialAsync(DeviceCredential credential);
    Task<DeviceCredential?> CredentialByFingerprintAsync(string fingerprint);
    Task SaveCredentialAsync(DeviceCredential credential);
    Task<DeviceChallenge?> ChallengeAsync(Guid id);
    Task InsertChallengeAsync(DeviceChallenge challenge);
    Task ConsumeChallengeAsync(Guid id);
    Task VerifyChallengeAsync(Guid id);
    Task<int> OutstandingChallengesAsync(Guid unitId);
    Task RevokeCredentialsAsync(Guid unitId);
    Task SetOfflineAsync(Guid unitId);
    Task HeartbeatAsync(Guid unitId, string? ipAddress);
}
