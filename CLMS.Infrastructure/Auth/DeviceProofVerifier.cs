using System.Security.Cryptography;
using CLMS.Application.UnitRegistry;
using CLMS.Shared.Security;

namespace CLMS.Infrastructure.Auth;

public sealed class DeviceProofVerifier : IDeviceProofVerifier
{
    /// <summary>Imports public-only P-256 material and requires canonical DER with no trailing data.</summary>
    public (string PublicKey, string Fingerprint) ValidatePublicKey(string publicKey)
    {
        try
        {
            var bytes = DeviceProtocol.Decode(publicKey);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);
            var parameters = key.ExportParameters(false);
            if (read != bytes.Length || parameters.Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                parameters.Q.X?.Length != 32 || parameters.Q.Y?.Length != 32 ||
                !key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(bytes)) throw new CryptographicException();
            return (publicKey, DeviceProtocol.Fingerprint(bytes));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        { throw new RegistryException("invalid_public_key", "A canonical ECDSA P-256 public key is required.", 400); }
    }

    /// <summary>Verifies exactly 64 signature bytes without exposing cryptographic exception details.</summary>
    public bool Verify(string publicKey, byte[] payload, string signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(DeviceProtocol.Decode(publicKey), out _);
            return key.VerifyData(payload, DeviceProtocol.Decode(signature, 64), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return false; }
    }
}
