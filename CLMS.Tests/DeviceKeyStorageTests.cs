using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using CLMS.Agent.Core;
using CLMS.Agent.Service;
using CLMS.Shared.Security;
using Xunit;

namespace CLMS.Tests;

public sealed class DeviceKeyStorageTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task DesktopIdentityCannotCreateWindowsServiceKeys()
    {
        if (!OperatingSystem.IsWindows() || WindowsIdentity.GetCurrent().IsSystem) return;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new WindowsDeviceKeyStore().CreateAsync(Guid.NewGuid().ToString("N"), default));
    }

    [NativeDeviceKeyFact]
    public async Task PersistentNativeKeyReopensAndSignsWithoutPrivateExport()
    {
        var id = Guid.NewGuid().ToString("N");
        IDeviceKeyStore Store() => OperatingSystem.IsWindows() ? new WindowsDeviceKeyStore()
            : OperatingSystem.IsMacOS() ? new MacDeviceKeyStore() : throw new PlatformNotSupportedException();
        var first = Store();
        try
        {
            var publicKey = await first.CreateAsync(id, default);
            var reopened = Store();
            Assert.Equal(publicKey, await reopened.PublicKeyAsync(id, default));
            var payload = RandomNumberGenerator.GetBytes(80);
            var signature = await reopened.SignAsync(id, payload, default);
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(DeviceProtocol.Decode(publicKey), out _);
            Assert.True(verifier.VerifyData(payload, DeviceProtocol.Decode(signature, 64), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            if (OperatingSystem.IsWindows())
            {
                using var native = CngKey.Open("CLMS.Agent.Device." + id, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
                Assert.Equal(CngExportPolicies.None, native.ExportPolicy);
                Assert.Throws<CryptographicException>(() => native.Export(CngKeyBlobFormat.Pkcs8PrivateBlob));
            }
            // The macOS provider refuses to open a key whose native extractable attribute isn't false.
        }
        finally { await first.DeleteAsync(id, default); }
    }
}

public sealed class NativeDeviceKeyFactAttribute : FactAttribute
{
    public NativeDeviceKeyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CLMS_NATIVE_DEVICE_KEYS") != "1" ||
            !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            Skip = "Set CLMS_NATIVE_DEVICE_KEYS=1 and run under LocalSystem on Windows or root on macOS for native key checks.";
    }
}
