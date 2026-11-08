using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CLMS.Agent.Core;

namespace CLMS.Agent.Service;

public sealed class ServiceVault : IAgentVault
{
    public static string StateDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CLMS", "Agent")
        : "/Library/Application Support/CLMS/Agent";
    private readonly string statePath;
    /// <summary>Prepares the service-owned state directory and applies platform-specific filesystem permissions.</summary>
    public ServiceVault()
    {
        Directory.CreateDirectory(StateDirectory);
        if (OperatingSystem.IsWindows())
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(StateDirectory).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        statePath = Path.Combine(StateDirectory, "credentials.dpapi");
    }
    /// <summary>Loads and decrypts the credential snapshot from Windows DPAPI or macOS System Keychain, clearing temporary plaintext byte buffers afterward.</summary>
    public async Task<AgentState?> ReadAsync(CancellationToken ct)
    {
        byte[]? plain = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!File.Exists(statePath)) return null;
                plain = ProtectedData.Unprotect(await File.ReadAllBytesAsync(statePath, ct), null, DataProtectionScope.LocalMachine);
            }
            else plain = SystemKeychain.Read();
            if (plain is null) return null;
            using var document = JsonDocument.Parse(plain);
            if (!document.RootElement.TryGetProperty("Version", out _))
            {
                var server = document.RootElement.TryGetProperty("ServerAddress", out var address) ? address.GetString() : null;
                return new AgentState(server, Phase: "RecoveryRequired");
            }
            return JsonSerializer.Deserialize<AgentState>(plain) ?? throw new IOException("Invalid service vault.");
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    /// <summary>Persists the entire snapshot through atomic file replacement on Windows or a Keychain item update on macOS, then clears temporary plaintext bytes.</summary>
    public async Task WriteAsync(AgentState state, CancellationToken ct)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.LocalMachine);
                var temporary = statePath + ".tmp";
                await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    await file.WriteAsync(protectedBytes, ct);
                    file.Flush(true);
                }
                File.Move(temporary, statePath, true);
            }
            else { ct.ThrowIfCancellationRequested(); SystemKeychain.Write(plain); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    // Native APIs keep secrets out of process arguments and the interactive user's login keychain.
    private static class SystemKeychain
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly byte[] Service = Encoding.UTF8.GetBytes("CLMS.Agent.Service.v1");
        private static readonly byte[] Account = Encoding.UTF8.GetBytes("device-state");
        /// <summary>Opens the macOS System Keychain rather than an interactive user's login keychain.</summary>
        private static IntPtr Open()
        {
            Check(SecKeychainOpen("/Library/Keychains/System.keychain", out var keychain));
            return keychain;
        }
        /// <summary>Reads the service's Keychain item and releases native buffers and item references; returns null only when the item does not exist.</summary>
        public static byte[]? Read()
        {
            var keychain = Open();
            try
            {
                var result = SecKeychainFindGenericPassword(keychain, (uint)Service.Length, Service, (uint)Account.Length, Account, out var length, out var data, out var item);
                if (result == -25300) return null;
                Check(result);
                try { var bytes = new byte[length]; Marshal.Copy(data, bytes, 0, checked((int)length)); return bytes; }
                finally { SecKeychainItemFreeContent(IntPtr.Zero, data); CFRelease(item); }
            }
            finally { CFRelease(keychain); }
        }
        /// <summary>Creates or updates the service's Keychain item and restricts access when a new item is created.</summary>
        public static void Write(byte[] bytes)
        {
            var keychain = Open();
            try
            {
                var found = SecKeychainFindGenericPassword(keychain, (uint)Service.Length, Service, (uint)Account.Length, Account, out _, out var data, out var item);
                if (found == 0)
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, data);
                    try { Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes)); }
                    finally { CFRelease(item); }
                }
                else
                {
                    if (found != -25300) Check(found);
                    Check(SecKeychainAddGenericPassword(keychain, (uint)Service.Length, Service, (uint)Account.Length, Account, (uint)bytes.Length, bytes, out item));
                    try { RestrictAccess(item); }
                    finally { CFRelease(item); }
                }
            }
            finally { CFRelease(keychain); }
        }
        /// <summary>Assigns a Keychain access list trusting the service executable and releases the temporary native references.</summary>
        private static void RestrictAccess(IntPtr item)
        {
            Check(SecTrustedApplicationCreateFromPath(Environment.ProcessPath!, out var trusted));
            var list = CFArrayCreate(IntPtr.Zero, [trusted], 1, IntPtr.Zero);
            var description = CFStringCreateWithCString(IntPtr.Zero, "CLMS service device credentials", 0x08000100);
            try
            {
                if (list == IntPtr.Zero || description == IntPtr.Zero) throw new IOException("Unable to allocate Keychain access controls.");
                Check(SecAccessCreate(description, list, out var access));
                try { Check(SecKeychainItemSetAccess(item, access)); }
                finally { CFRelease(access); }
            }
            finally
            {
                if (description != IntPtr.Zero) CFRelease(description);
                if (list != IntPtr.Zero) CFRelease(list);
                CFRelease(trusted);
            }
        }
        /// <summary>Converts a failing Security.framework status into an exception containing only the numeric error code.</summary>
        private static void Check(int status) { if (status != 0) throw new IOException($"System Keychain operation failed ({status})."); }
        /// <summary>Opens a native Keychain reference that the caller must release.</summary>
        [DllImport(Security)] private static extern int SecKeychainOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr keychain);
        /// <summary>Locates the service password item and returns caller-owned password data and item references.</summary>
        [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr data, out IntPtr item);
        /// <summary>Creates a native password item without passing secrets through command-line arguments.</summary>
        [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint length, byte[] data, out IntPtr item);
        /// <summary>Replaces an existing Keychain item's stored snapshot data.</summary>
        [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
        /// <summary>Releases password data allocated by a Keychain lookup.</summary>
        [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
        /// <summary>Creates a trusted-application reference for the installed service executable.</summary>
        [DllImport(Security)] private static extern int SecTrustedApplicationCreateFromPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr trusted);
        /// <summary>Creates a Keychain access-control object from the trusted-application list.</summary>
        [DllImport(Security)] private static extern int SecAccessCreate(IntPtr description, IntPtr trusted, out IntPtr access);
        /// <summary>Applies the prepared access controls to a Keychain item.</summary>
        [DllImport(Security)] private static extern int SecKeychainItemSetAccess(IntPtr item, IntPtr access);
        /// <summary>Creates a Core Foundation array used to pass trusted application references.</summary>
        [DllImport(CoreFoundation)] private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, nint count, IntPtr callbacks);
        /// <summary>Creates the native UTF-8 description used by the Keychain access-control object.</summary>
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
        /// <summary>Releases an owned Core Foundation or compatible Security.framework reference.</summary>
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    }
}
