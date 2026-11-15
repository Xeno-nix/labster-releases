using System.Buffers.Binary;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Read-only Darwin boot, console, and audit-session metadata. No audit policy or system settings are changed.</summary>
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("maccatalyst")]
internal static class MacAccessNative
{
    private const string System = "/usr/lib/libSystem.B.dylib";
    private const string Bsm = "/usr/lib/libbsm.dylib";
    private const string Configuration = "/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>Uses a stable boot UUID for deduplication and the kernel's UTC boot time for the Startup event.</summary>
    public static MacBootInfo Boot()
    {
        var uuid = ReadSysctl("kern.bootsessionuuid", 128);
        if (!Guid.TryParse(Encoding.UTF8.GetString(uuid).TrimEnd('\0'), out var id) || id == Guid.Empty)
            throw new InvalidDataException("The kernel boot identity is unavailable.");
        var time = ReadSysctl("kern.boottime", 16);
        if (time.Length != 16) throw new InvalidDataException("Unexpected native boot-time layout.");
        var seconds = BinaryPrimitives.ReadInt64LittleEndian(time);
        var micros = BinaryPrimitives.ReadInt32LittleEndian(time.AsSpan(8));
        if (micros is < 0 or >= 1000000) throw new InvalidDataException("Invalid kernel boot time.");
        return new(id, DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(micros * 10L));
    }

    /// <summary>Reads the companion's real audit session; only authenticated local graphical sessions are eligible.</summary>
    public static MacSessionInfo Session()
    {
        Check(getaudit_addr(out var info, Marshal.SizeOf<AuditInfo>()));
        return new(geteuid(), info.SessionId, Graphical(info.Flags));
    }

    /// <summary>Checks console UID without retaining or emitting the returned username.</summary>
    public static bool IsConsoleUser(uint uid)
    {
        var name = SCDynamicStoreCopyConsoleUser(IntPtr.Zero, out var consoleUid, out _);
        if (name == IntPtr.Zero) return false;
        try { return uid != 0 && uid == consoleUid; }
        finally { CFRelease(name); }
    }

    /// <summary>Binds the report to kernel-provided socket credentials and graphical audit-session flags.</summary>
    public static MacPeerInfo Peer(Socket socket)
    {
        var descriptor = socket.Handle.ToInt32();
        Check(getpeereid(descriptor, out var uid, out _));
        uint size = 32;
        // Darwin sys/un.h: SOL_LOCAL=0, LOCAL_PEERTOKEN=6. The token is eight uint32 values.
        Check(getsockopt(descriptor, 0, 6, out var token, ref size));
        if (size != 32 || token.EffectiveUid != uid || token.SessionId <= 0)
            throw new UnauthorizedAccessException("The local monitoring peer cannot be verified.");
        var info = new AuditInfo { SessionId = token.SessionId };
        // A_GETSINFO_ADDR=32 reads metadata for an existing session; it does not enable audit logging.
        Check(auditon(32, ref info, Marshal.SizeOf<AuditInfo>()));
        if (info.SessionId != token.SessionId || !Graphical(info.Flags))
            throw new UnauthorizedAccessException("A local authenticated graphical session is required.");
        return new(uid, token.SessionId, IsConsoleUser(uid));
    }

    /// <summary>Excludes the initial daemon session, unauthenticated loginwindow, and remote SSH/login sessions.</summary>
    private static bool Graphical(ulong flags) => (flags & 0x0010) != 0 && (flags & 0x4000) != 0 && (flags & 0x1001) == 0;

    /// <summary>Reads only fixed, bounded kernel metadata values.</summary>
    private static byte[] ReadSysctl(string name, int maximum)
    {
        var bytes = new byte[maximum];
        nuint size = (nuint)bytes.Length;
        Check(sysctlbyname(name, bytes, ref size, IntPtr.Zero, 0));
        if (size == 0 || size > (nuint)bytes.Length) throw new InvalidDataException("Invalid kernel metadata size.");
        return bytes[..checked((int)size)];
    }

    /// <summary>Preserves the numeric native error for controlled diagnostics, never an incoming payload.</summary>
    private static void Check(int result)
    { if (result != 0) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    // bsm/audit.h: 32-bit UID/mask/terminal fields, then ASID at offset 36 and 64-bit flags at offset 40.
    [StructLayout(LayoutKind.Sequential)]
    private struct AuditInfo
    {
        public uint AuditUid, SuccessMask, FailureMask, Port, AddressType, Address0, Address1, Address2, Address3;
        public int SessionId;
        public ulong Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AuditToken
    {
        public uint AuditUid, EffectiveUid, EffectiveGid, RealUid, RealGid, ProcessId;
        public int SessionId;
        public uint ProcessVersion;
    }

    [DllImport(System, SetLastError = true)] private static extern int sysctlbyname([MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value, ref nuint size, IntPtr newValue, nuint newSize);
    [DllImport(System)] private static extern uint geteuid();
    [DllImport(System, SetLastError = true)] private static extern int getpeereid(int descriptor, out uint uid, out uint gid);
    [DllImport(System, SetLastError = true)] private static extern int getsockopt(int descriptor, int level, int option, out AuditToken value, ref uint length);
    [DllImport(Bsm, SetLastError = true)] private static extern int getaudit_addr(out AuditInfo info, int size);
    [DllImport(Bsm, SetLastError = true)] private static extern int auditon(int command, ref AuditInfo info, int size);
    [DllImport(Configuration)] private static extern IntPtr SCDynamicStoreCopyConsoleUser(IntPtr store, out uint uid, out uint gid);
    [DllImport(Foundation)] private static extern void CFRelease(IntPtr value);
}

internal sealed record MacSessionInfo(uint UserId, int AuditSessionId, bool IsGraphical);
