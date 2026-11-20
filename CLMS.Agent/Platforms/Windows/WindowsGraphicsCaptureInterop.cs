using System.Runtime.InteropServices;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX.Direct3D11;

namespace CLMS.Agent.Platforms.Windows;

/// <summary>Native monitor and D3D interop needed by Windows Graphics Capture; no extra graphics package.</summary>
internal static class WindowsGraphicsCaptureInterop
{
    public static nint PrimaryMonitor()
    {
        var monitor = MonitorFromPoint(default, 1); // MONITOR_DEFAULTTOPRIMARY
        return monitor != 0 ? monitor : throw new InvalidOperationException("No primary display is available.");
    }

    public static GraphicsCaptureItem CreateItem(nint monitor)
    {
        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        nint className = 0, factory = 0, item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, (uint)runtimeClass.Length, out className));
            var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, in interopId, out factory));
            // IUnknown's three slots, then CreateForWindow, then CreateForMonitor.
            var vtable = Marshal.ReadIntPtr(factory);
            var create = Marshal.GetDelegateForFunctionPointer<CreateForMonitor>(Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size));
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            Marshal.ThrowExceptionForHR(create(factory, monitor, in itemId, out item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            if (className != 0) WindowsDeleteString(className);
        }
    }

    public static IDirect3DDevice CreateDevice()
    {
        nint d3dDevice = 0, context = 0, dxgiDevice = 0, inspectable = 0;
        try
        {
            var result = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out d3dDevice, out _, out context);
            if (result < 0)
            {
                if (context != 0) { Marshal.Release(context); context = 0; }
                if (d3dDevice != 0) { Marshal.Release(d3dDevice); d3dDevice = 0; }
                // WARP is a software D3D device, not the old GDI screenshot path.
                result = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out d3dDevice, out _, out context);
            }
            Marshal.ThrowExceptionForHR(result);
            var dxgiId = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in dxgiId, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgiDevice != 0) Marshal.Release(dxgiDevice);
            if (context != 0) Marshal.Release(context);
            if (d3dDevice != 0) Marshal.Release(d3dDevice);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForMonitor(nint self, nint monitor, in Guid iid, out nint item);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int WindowsCreateString(string value, uint length, out nint hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint hstring);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint className, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint featureLevels,
        uint featureLevelCount, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
