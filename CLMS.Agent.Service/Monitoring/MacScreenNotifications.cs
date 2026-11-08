using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CLMS.Agent.Service.Monitoring;

/// <summary>Best-effort session screen notifications. Must be created and pumped on the companion's main thread.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacScreenNotifications : IDisposable
{
    private const string Framework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private readonly IntPtr center, lockedName, unlockedName, mode;
    private readonly Notification callback;
    private GCHandle observer;
    private bool disposed;

    /// <summary>Subscribes only to lock/unlock implementation-detail names, never treats wake as unlock.</summary>
    public MacScreenNotifications(Action<bool> observe)
    {
        center = CFNotificationCenterGetDistributedCenter();
        lockedName = CFStringCreateWithCString(IntPtr.Zero, "com.apple.screenIsLocked", 0x08000100);
        unlockedName = CFStringCreateWithCString(IntPtr.Zero, "com.apple.screenIsUnlocked", 0x08000100);
        mode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", 0x08000100);
        callback = (_, _, name, _, _) =>
        {
            // No managed exception may unwind through a Core Foundation callback.
            try
            {
                if (CFEqual(name, lockedName)) observe(true);
                else if (CFEqual(name, unlockedName)) observe(false);
            }
            catch { /* The owning loop reports controlled monitoring health errors. */ }
        };
        if (center == IntPtr.Zero || lockedName == IntPtr.Zero || unlockedName == IntPtr.Zero || mode == IntPtr.Zero)
        { Dispose(); throw new IOException("Screen notification initialization failed."); }
        observer = GCHandle.Alloc(this);
        CFNotificationCenterAddObserver(center, GCHandle.ToIntPtr(observer), callback, lockedName, IntPtr.Zero, 4);
        CFNotificationCenterAddObserver(center, GCHandle.ToIntPtr(observer), callback, unlockedName, IntPtr.Zero, 4);
    }

    /// <summary>Pumps the main run loop for a bounded interval so cancellation and console changes remain responsive.</summary>
    public void Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var result = CFRunLoopRunInMode(mode, 0.25, true);
        if (result is 1 or 2) throw new IOException("The screen notification run loop is unavailable.");
        GC.KeepAlive(callback);
    }

    /// <summary>Removes observers before releasing their delegate roots and owned CFString references.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (observer.IsAllocated)
        {
            CFNotificationCenterRemoveEveryObserver(center, GCHandle.ToIntPtr(observer));
            observer.Free();
        }
        foreach (var value in new[] { lockedName, unlockedName, mode }) if (value != IntPtr.Zero) CFRelease(value);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Notification(IntPtr center, IntPtr observer, IntPtr name, IntPtr obj, IntPtr info);
    [DllImport(Framework)] private static extern IntPtr CFNotificationCenterGetDistributedCenter();
    [DllImport(Framework)] private static extern void CFNotificationCenterAddObserver(IntPtr center, IntPtr observer, Notification callback, IntPtr name, IntPtr obj, int suspensionBehavior);
    [DllImport(Framework)] private static extern void CFNotificationCenterRemoveEveryObserver(IntPtr center, IntPtr observer);
    [DllImport(Framework)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(Framework)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFEqual(IntPtr first, IntPtr second);
    [DllImport(Framework)] private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);
    [DllImport(Framework)] private static extern void CFRelease(IntPtr value);
}
