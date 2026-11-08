using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using CLMS.Agent.Core;
using CLMS.Shared.Security;

namespace CLMS.Agent.Service;

/// <summary>Persistent, non-exportable CNG machine signing keys owned by the LocalSystem service.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceKeyStore : IDeviceKeyStore
{
    private static string Name(string id) => Guid.TryParseExact(id, "N", out _) ? "CLMS.Agent.Device." + id : throw new ArgumentException("Invalid key reference.");

    /// <summary>Creates signing-only P-256 material and restricts its DACL before returning its public key.</summary>
    public Task<string> CreateAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!WindowsIdentity.GetCurrent().IsSystem) throw new UnauthorizedAccessException("Device keys require the LocalSystem service identity.");
        using var key = CngKey.Exists(Name(keyId), CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey)
            ? Open(keyId)
            : CngKey.Create(CngAlgorithm.ECDsaP256, Name(keyId), new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider, ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing, KeyCreationOptions = CngKeyCreationOptions.MachineKey
            });
        var descriptor = new RawSecurityDescriptor("O:SYG:SYD:P(A;;GA;;;SY)");
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);
        key.SetProperty(new CngProperty("Security Descr", binary, CngPropertyOptions.Persist | (CngPropertyOptions)4));
        using var signer = new ECDsaCng(key);
        return Task.FromResult(DeviceProtocol.Encode(signer.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Reopens an existing machine key without creating or overwriting credentials.</summary>
    private static CngKey Open(string id)
    {
        var key = CngKey.Open(Name(id), CngProvider.MicrosoftSoftwareKeyStorageProvider,
            CngKeyOpenOptions.MachineKey | CngKeyOpenOptions.Silent);
        if (key.ExportPolicy == CngExportPolicies.None && key.Algorithm == CngAlgorithm.ECDsaP256 &&
            key.KeyUsage == CngKeyUsages.Signing && key.IsMachineKey) return key;
        key.Dispose();
        throw new CryptographicException("The device key does not meet service storage requirements.");
    }

    public Task<string> PublicKeyAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var key = Open(keyId);
        using var signer = new ECDsaCng(key);
        return Task.FromResult(DeviceProtocol.Encode(signer.ExportSubjectPublicKeyInfo()));
    }

    public Task<string> SignAsync(string keyId, byte[] payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var key = Open(keyId);
        using var signer = new ECDsaCng(key);
        return Task.FromResult(DeviceProtocol.Encode(signer.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }

    public Task DeleteAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (CngKey.Exists(Name(keyId), CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey))
        { using var key = Open(keyId); key.Delete(); }
        return Task.CompletedTask;
    }
}
