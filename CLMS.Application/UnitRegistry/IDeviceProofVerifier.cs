namespace CLMS.Application.UnitRegistry;

public interface IDeviceProofVerifier
{
    /// <summary>Validates and canonicalizes an ES256 SubjectPublicKeyInfo and returns its fingerprint.</summary>
    (string PublicKey, string Fingerprint) ValidatePublicKey(string publicKey);
    /// <summary>Checks a fixed-width P1363 ECDSA/SHA-256 signature, returning false for malformed input.</summary>
    bool Verify(string publicKey, byte[] payload, string signature);
}
