using System.Security.Cryptography;
using CLMS.Agent.Core;
using CLMS.Agent.Service;
using CLMS.Shared.Security;

// Run create/verify in separate processes, optionally across a reboot. This harness
// touches only its supplied test key, never the service vault or server database.
if (args.Length != 2 || args[0] is not ("create" or "verify" or "delete") ||
    !Guid.TryParseExact(args[1], "N", out _))
    throw new ArgumentException("Usage: CLMS.DeviceKeys.Checks create|verify|delete TEST_GUID_N");
IDeviceKeyStore store = OperatingSystem.IsWindows() ? new WindowsDeviceKeyStore()
    : OperatingSystem.IsMacOS() ? new MacDeviceKeyStore() : throw new PlatformNotSupportedException();
if (args[0] == "delete")
{
    await store.DeleteAsync(args[1], default);
    Console.WriteLine("Test key deleted.");
    return;
}
var publicKey = args[0] == "create" ? await store.CreateAsync(args[1], default) : await store.PublicKeyAsync(args[1], default);
var payload = RandomNumberGenerator.GetBytes(64);
var signature = await store.SignAsync(args[1], payload, default);
using var verifier = ECDsa.Create();
verifier.ImportSubjectPublicKeyInfo(DeviceProtocol.Decode(publicKey), out _);
if (!verifier.VerifyData(payload, DeviceProtocol.Decode(signature, 64), HashAlgorithmName.SHA256,
    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new CryptographicException("Native signing verification failed.");
if (OperatingSystem.IsWindows())
{
    using var key = CngKey.Open("CLMS.Agent.Device." + args[1], CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
    try
    {
        var unexpected = key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
        CryptographicOperations.ZeroMemory(unexpected);
        throw new InvalidOperationException("The private key was exportable.");
    }
    catch (CryptographicException) { /* Expected: private export is forbidden. */ }
}
Console.WriteLine("Native signing passed. Public fingerprint: " + DeviceProtocol.Fingerprint(DeviceProtocol.Decode(publicKey)));
