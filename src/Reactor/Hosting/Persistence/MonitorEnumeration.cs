using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Enumerates the active display layout for the persistence fingerprint
/// (spec 036 §8). Wraps <c>EnumDisplayMonitors</c> + <c>GetMonitorInfo</c>;
/// returns a stable order so the fingerprint round-trips deterministically.
/// </summary>
internal static class MonitorEnumeration
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    public static IReadOnlyList<MonitorRect> Snapshot()
    {
        var list = new List<MonitorRect>(2);
        try
        {
            EnumDisplayMonitors(0, 0, (nint h, nint _, ref RECT _, nint _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(h, ref info))
                {
                    var r = info.rcMonitor;
                    list.Add(new MonitorRect(info.szDevice, r.Left, r.Top, r.Right, r.Bottom));
                }
                return true;
            }, 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] MonitorEnumeration.Snapshot failed: {ex.GetType().Name}: {ex.Message}");
        }
        return list;
    }
}
