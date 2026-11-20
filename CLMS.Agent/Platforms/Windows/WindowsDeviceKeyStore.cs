using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using CLMS.Agent.Core;
using CLMS.Shared.Security;

namespace CLMS.Agent.Service;

/// <summary>Persistent, non-exportable CNG user signing keys owned by the interactive account.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceKeyStore : IDeviceKeyStore
{
    private static string Name(string id) => Guid.TryParseExact(id, "N", out _) ? "CLMS.Agent.Device." + id : throw new ArgumentException("Invalid key reference.");

    /// <summary>Creates signing-only P-256 material and restricts its DACL before returning its public key.</summary>
    public Task<string> CreateAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var key = CngKey.Exists(Name(keyId), CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey)
            ? Open(keyId)
            : CngKey.Create(CngAlgorithm.ECDsaP256, Name(keyId), new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider, ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing, KeyCreationOptions = CngKeyCreationOptions.None
            });
        var descriptor = new RawSecurityDescriptor($"O:{WindowsIdentity.GetCurrent().User!.Value}G:{WindowsIdentity.GetCurrent().User!.Value}D:P(A;;GA;;;{WindowsIdentity.GetCurrent().User!.Value})");
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
            CngKeyOpenOptions.UserKey | CngKeyOpenOptions.Silent);
        if (key.ExportPolicy == CngExportPolicies.None && key.Algorithm == CngAlgorithm.ECDsaP256 &&
            key.KeyUsage == CngKeyUsages.Signing && !key.IsMachineKey) return key;
        key.Dispose();
        throw new CryptographicException("The device key does not meet user storage requirements.");
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
        if (CngKey.Exists(Name(keyId), CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey))
        { using var key = Open(keyId); key.Delete(); }
        return Task.CompletedTask;
    }
}
