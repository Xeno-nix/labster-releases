namespace CLMS.Agent.Core;

/// <summary>Service-identity private-key operations. Implementations must never export private material.</summary>
public interface IDeviceKeyStore
{
    /// <summary>Creates a persistent signing key, or opens the same key after an interrupted creation.</summary>
    Task<string> CreateAsync(string keyId, CancellationToken ct);
    /// <summary>Loads only the public SPKI; absence is a recovery condition rather than permission to replace a key.</summary>
    Task<string> PublicKeyAsync(string keyId, CancellationToken ct);
    /// <summary>Signs bytes using the persistent private key and returns an ES256 P1363 signature.</summary>
    Task<string> SignAsync(string keyId, byte[] payload, CancellationToken ct);
    /// <summary>Deletes retired or rejected local key material after durable state transition.</summary>
    Task DeleteAsync(string keyId, CancellationToken ct);
}
