using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CLMS.Agent.Core;
using CLMS.Shared.Security;

namespace CLMS.Agent.Service;

/// <summary>Non-exportable System Keychain keys for a signed root launch daemon, independent of user login.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacDeviceKeyStore : IDeviceKeyStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly IntPtr SecurityLibrary = NativeLibrary.Load(Security);
    private static readonly IntPtr FoundationLibrary = NativeLibrary.Load(CF);
    private static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(
        name.StartsWith("kCF", StringComparison.Ordinal) ? FoundationLibrary : SecurityLibrary, name));

    private static byte[] Tag(string id) => Guid.TryParseExact(id, "N", out _)
        ? Encoding.UTF8.GetBytes("CLMS.Agent.Device." + id) : throw new ArgumentException("Invalid key reference.");

    /// <summary>Creates permanent, sensitive, non-exportable material with service-specific Keychain access controls.</summary>
    public Task<string> CreateAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (geteuid() != 0) throw new UnauthorizedAccessException("Device keys require the root service identity.");
        var existing = Find(keyId, false);
        if (existing != IntPtr.Zero)
        {
            try { return Task.FromResult(Public(existing)); }
            finally { CFRelease(existing); }
        }
        Check(SecKeychainOpen("/Library/Keychains/System.keychain", out var keychain));
        var parameters = Dictionary();
        var privateAttributes = Dictionary();
        var tag = Data(Tag(keyId));
        var bits = 256;
        var number = CFNumberCreate(IntPtr.Zero, 9, ref bits);
        IntPtr trusted = IntPtr.Zero, applications = IntPtr.Zero, description = IntPtr.Zero, access = IntPtr.Zero;
        IntPtr privateKey = IntPtr.Zero, publicKey = IntPtr.Zero;
        try
        {
            // macOS uses the executable's code requirement for trusted-application identity.
            // Deployment must retain a stable Developer ID designated requirement across upgrades.
            Check(SecTrustedApplicationCreateFromPath(Environment.ProcessPath!, out trusted));
            applications = CFArrayCreate(IntPtr.Zero, [trusted], 1,
                NativeLibrary.GetExport(FoundationLibrary, "kCFTypeArrayCallBacks"));
            description = CFStringCreateWithCString(IntPtr.Zero, "CLMS service device signing key", 0x08000100);
            Check(SecAccessCreate(description, applications, out access));
            Set(parameters, "kSecAttrKeyType", Constant("kSecAttrKeyTypeECSECPrimeRandom"));
            Set(parameters, "kSecAttrKeySizeInBits", number);
            Set(parameters, "kSecUseKeychain", keychain);
            Set(privateAttributes, "kSecAttrApplicationTag", tag);
            Set(privateAttributes, "kSecAttrIsPermanent", Constant("kCFBooleanTrue"));
            Set(privateAttributes, "kSecAttrIsExtractable", Constant("kCFBooleanFalse"));
            Set(privateAttributes, "kSecAttrIsSensitive", Constant("kCFBooleanTrue"));
            Set(privateAttributes, "kSecAttrCanSign", Constant("kCFBooleanTrue"));
            Set(privateAttributes, "kSecAttrAccess", access);
            Set(parameters, "kSecPrivateKeyAttrs", privateAttributes);
            // This macOS API supports explicit System Keychain placement and ACLs;
            // it avoids the per-user data-protection Keychain used by interactive apps.
            Check(SecKeyGeneratePair(parameters, out publicKey, out privateKey));
            RequireNonExportable(privateKey);
            return Task.FromResult(Public(privateKey));
        }
        finally
        {
            foreach (var item in new[] { privateKey, publicKey, access, description, applications, trusted, number, tag, privateAttributes, parameters, keychain })
                if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    public Task<string> PublicKeyAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = Find(keyId, true);
        try { return Task.FromResult(Public(key)); }
        finally { CFRelease(key); }
    }

    /// <summary>Signs without exporting the key, then converts Security.framework DER into fixed-width P1363.</summary>
    public Task<string> SignAsync(string keyId, byte[] payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = Find(keyId, true);
        var message = Data(payload);
        IntPtr signature = IntPtr.Zero, error = IntPtr.Zero;
        try
        {
            signature = SecKeyCreateSignature(key, Constant("kSecKeyAlgorithmECDSASignatureMessageX962SHA256"), message, out error);
            if (signature == IntPtr.Zero) throw new CryptographicException("System Keychain signing failed.");
            var reader = new AsnReader(Bytes(signature), AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            var r = Integer(sequence.ReadIntegerBytes().Span);
            var s = Integer(sequence.ReadIntegerBytes().Span);
            sequence.ThrowIfNotEmpty(); reader.ThrowIfNotEmpty();
            return Task.FromResult(DeviceProtocol.Encode(r.Concat(s).ToArray()));
        }
        finally
        {
            if (error != IntPtr.Zero) CFRelease(error);
            if (signature != IntPtr.Zero) CFRelease(signature);
            CFRelease(message); CFRelease(key);
        }
    }

    public Task DeleteAsync(string keyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var query = Query(keyId);
        var result = SecItemDelete(query.Value);
        if (result != -25300) Check(result);
        return Task.CompletedTask;
    }

    private static byte[] Integer(ReadOnlySpan<byte> integer)
    {
        if (integer.Length == 33 && integer[0] == 0) integer = integer[1..];
        if (integer.Length > 32) throw new CryptographicException("Invalid ECDSA signature.");
        var result = new byte[32]; integer.CopyTo(result.AsSpan(32 - integer.Length)); return result;
    }

    private static string Public(IntPtr privateKey)
    {
        var publicKey = SecKeyCopyPublicKey(privateKey);
        if (publicKey == IntPtr.Zero) throw new CryptographicException("Unable to read public key.");
        IntPtr bytes = IntPtr.Zero, error = IntPtr.Zero;
        try
        {
            bytes = SecKeyCopyExternalRepresentation(publicKey, out error); // Public key only.
            if (bytes == IntPtr.Zero) throw new CryptographicException("Unable to read public key.");
            var point = Bytes(bytes);
            if (point.Length != 65 || point[0] != 4) throw new CryptographicException("Invalid P-256 public key.");
            using var encoder = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..65] }
            });
            return DeviceProtocol.Encode(encoder.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            if (bytes != IntPtr.Zero) CFRelease(bytes);
            if (error != IntPtr.Zero) CFRelease(error);
            CFRelease(publicKey);
        }
    }

    private static IntPtr Find(string keyId, bool required)
    {
        using var query = Query(keyId);
        Set(query.Value, "kSecReturnRef", Constant("kCFBooleanTrue"));
        Set(query.Value, "kSecMatchLimit", Constant("kSecMatchLimitOne"));
        var result = SecItemCopyMatching(query.Value, out var key);
        if (result == -25300 && !required) return IntPtr.Zero;
        Check(result);
        try { RequireNonExportable(key); }
        catch { CFRelease(key); throw; }
        return key;
    }

    /// <summary>Fails closed if the provider did not honor non-exportable private-key creation.</summary>
    private static void RequireNonExportable(IntPtr key)
    {
        var attributes = SecKeyCopyAttributes(key);
        if (attributes == IntPtr.Zero) throw new CryptographicException("Unable to inspect device key protection.");
        try
        {
            if (CFDictionaryGetValue(attributes, Constant("kSecAttrIsExtractable")) != Constant("kCFBooleanFalse"))
                throw new CryptographicException("The device key must be non-exportable.");
        }
        finally { CFRelease(attributes); }
    }

    private static QueryHandle Query(string keyId)
    {
        Check(SecKeychainOpen("/Library/Keychains/System.keychain", out var keychain));
        var query = Dictionary();
        var tag = Data(Tag(keyId));
        var list = CFArrayCreate(IntPtr.Zero, [keychain], 1, NativeLibrary.GetExport(FoundationLibrary, "kCFTypeArrayCallBacks"));
        try
        {
            Set(query, "kSecClass", Constant("kSecClassKey"));
            Set(query, "kSecAttrKeyClass", Constant("kSecAttrKeyClassPrivate"));
            Set(query, "kSecAttrApplicationTag", tag);
            Set(query, "kSecMatchSearchList", list);
            Set(query, "kSecUseAuthenticationUI", Constant("kSecUseAuthenticationUIFail"));
            return new(query);
        }
        catch { CFRelease(query); throw; }
        finally { CFRelease(list); CFRelease(tag); CFRelease(keychain); }
    }

    private sealed class QueryHandle(IntPtr value) : IDisposable
    { public IntPtr Value => value; public void Dispose() => CFRelease(value); }
    private static IntPtr Dictionary() => CFDictionaryCreateMutable(IntPtr.Zero, 0,
        NativeLibrary.GetExport(FoundationLibrary, "kCFTypeDictionaryKeyCallBacks"),
        NativeLibrary.GetExport(FoundationLibrary, "kCFTypeDictionaryValueCallBacks"));
    private static void Set(IntPtr dictionary, string key, IntPtr value) => CFDictionarySetValue(dictionary, Constant(key), value);
    private static IntPtr Data(byte[] bytes) => CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
    private static byte[] Bytes(IntPtr data)
    { var bytes = new byte[checked((int)CFDataGetLength(data))]; Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length); return bytes; }
    private static void Check(int status)
    { if (status != 0) throw new CryptographicException($"System Keychain operation failed ({status})."); }

    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern uint geteuid();
    [DllImport(Security)] private static extern int SecKeychainOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr keychain);
    [DllImport(Security)] private static extern int SecTrustedApplicationCreateFromPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr trusted);
    [DllImport(Security)] private static extern int SecAccessCreate(IntPtr description, IntPtr applications, out IntPtr access);
    [DllImport(Security)] private static extern int SecKeyGeneratePair(IntPtr parameters, out IntPtr publicKey, out IntPtr privateKey);
    [DllImport(Security)] private static extern IntPtr SecKeyCopyPublicKey(IntPtr privateKey);
    [DllImport(Security)] private static extern IntPtr SecKeyCopyAttributes(IntPtr key);
    [DllImport(Security)] private static extern IntPtr SecKeyCopyExternalRepresentation(IntPtr key, out IntPtr error);
    [DllImport(Security)] private static extern IntPtr SecKeyCreateSignature(IntPtr key, IntPtr algorithm, IntPtr data, out IntPtr error);
    [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Security)] private static extern int SecItemDelete(IntPtr query);
    [DllImport(CF)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CF)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CF)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CF)] private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, nint count, IntPtr callbacks);
    [DllImport(CF)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CF)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CF)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CF)] private static extern IntPtr CFNumberCreate(IntPtr allocator, nint type, ref int value);
    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(CF)] private static extern void CFRelease(IntPtr value);
}
