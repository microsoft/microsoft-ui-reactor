using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting.Messaging;
using Windows.Foundation;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Process-wide display enumeration helpers for Reactor windowing.
/// </summary>
public static class ReactorDisplay
{
    private static readonly object s_lock = new();
    private static readonly List<DisplayMonitorRegistration> s_registrations = new();
    private static WindowMessageMonitor? s_hostMonitor;
    private static IReadOnlyList<DisplayInfo> s_displays = SnapshotDisplays();

    /// <summary>
    /// Snapshot of currently active displays. Bounds and work areas are projected
    /// into DIPs using each monitor's own DPI; on mixed-DPI desktops non-primary
    /// X/Y values are approximate because Windows has no single global DIP space.
    /// </summary>
    public static IReadOnlyList<DisplayInfo> Displays
    {
        get
        {
            var current = Volatile.Read(ref s_displays);
            if (current.Count > 0) return current;
            var next = SnapshotDisplays();
            Volatile.Write(ref s_displays, next);
            return next;
        }
    }

    /// <summary>The primary display in the current layout.</summary>
    public static DisplayInfo Primary
    {
        get
        {
            var displays = Displays;
            return displays.FirstOrDefault(d => d.IsPrimary)
                ?? displays.FirstOrDefault()
                ?? throw new InvalidOperationException("No displays are available.");
        }
    }

    /// <summary>
    /// Returns the display whose DIP bounds contain <paramref name="dipX"/> / <paramref name="dipY"/>,
    /// or the nearest display when the point is outside all bounds.
    /// </summary>
    public static DisplayInfo NearestTo(double dipX, double dipY)
    {
        var displays = Displays;
        if (displays.Count == 0) throw new InvalidOperationException("No displays are available.");

        for (int i = 0; i < displays.Count; i++)
        {
            if (Contains(displays[i].BoundsDip, dipX, dipY))
                return displays[i];
        }

        DisplayInfo best = displays[0];
        double bestDistance = DistanceSquaredToRect(best.BoundsDip, dipX, dipY);
        for (int i = 1; i < displays.Count; i++)
        {
            double distance = DistanceSquaredToRect(displays[i].BoundsDip, dipX, dipY);
            if (distance < bestDistance)
            {
                best = displays[i];
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Fires on the UI thread when Windows reports a display layout change.</summary>
    public static event EventHandler? DisplayLayoutChanged;

    internal static void RegisterWindowMonitor(ReactorWindow window, WindowMessageMonitor monitor)
    {
        lock (s_lock)
        {
            for (int i = 0; i < s_registrations.Count; i++)
            {
                if (ReferenceEquals(s_registrations[i].Window, window)) return;
            }

            s_registrations.Add(new DisplayMonitorRegistration(window, monitor));
            if (s_hostMonitor is null)
                AttachHostMonitor(monitor);
        }
    }

    internal static void UnregisterWindowMonitor(ReactorWindow window)
    {
        lock (s_lock)
        {
            WindowMessageMonitor? removedMonitor = null;
            for (int i = s_registrations.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(s_registrations[i].Window, window)) continue;
                removedMonitor = s_registrations[i].Monitor;
                s_registrations.RemoveAt(i);
            }

            if (removedMonitor is not null && ReferenceEquals(s_hostMonitor, removedMonitor))
            {
                DetachHostMonitor(removedMonitor);
                if (s_registrations.Count > 0)
                    AttachHostMonitor(s_registrations[0].Monitor);
            }
        }
    }

    private static void AttachHostMonitor(WindowMessageMonitor monitor)
    {
        monitor.MessageReceived += OnHostWindowMessage;
        s_hostMonitor = monitor;
    }

    private static void DetachHostMonitor(WindowMessageMonitor monitor)
    {
        monitor.MessageReceived -= OnHostWindowMessage;
        if (ReferenceEquals(s_hostMonitor, monitor))
            s_hostMonitor = null;
    }

    private static void OnHostWindowMessage(object? sender, WindowMessageEventArgs args)
    {
        if (args.Msg != WindowMessageMonitor.WM_DISPLAYCHANGE) return;
        Volatile.Write(ref s_displays, SnapshotDisplays());
        DisplayLayoutChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool Contains(Rect rect, double x, double y)
        => x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;

    private static double DistanceSquaredToRect(Rect rect, double x, double y)
    {
        double dx = x < rect.X ? rect.X - x : x > rect.X + rect.Width ? x - (rect.X + rect.Width) : 0;
        double dy = y < rect.Y ? rect.Y - y : y > rect.Y + rect.Height ? y - (rect.Y + rect.Height) : 0;
        return dx * dx + dy * dy;
    }

    private static IReadOnlyList<DisplayInfo> SnapshotDisplays()
    {
        var list = new List<DisplayInfo>(2);
        _ = NativeDisplay.EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref NativeDisplay.RECT _, nint _) =>
        {
            var info = new NativeDisplay.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeDisplay.MONITORINFOEX>() };
            if (!NativeDisplay.GetMonitorInfo(hMonitor, ref info)) return true;

            uint dpi = NativeDisplay.GetEffectiveDpi(hMonitor);
            string id = string.IsNullOrWhiteSpace(info.szDevice) ? $"monitor:{hMonitor:X}" : info.szDevice;
            list.Add(new DisplayInfo
            {
                Id = id,
                IsPrimary = (info.dwFlags & NativeDisplay.MONITORINFOF_PRIMARY) != 0,
                BoundsDip = RectToDip(info.rcMonitor, dpi),
                WorkAreaDip = RectToDip(info.rcWork, dpi),
                Dpi = dpi,
            });
            return true;
        }, 0);

        return list
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.BoundsDip.X)
            .ThenBy(d => d.BoundsDip.Y)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static Rect RectToDip(NativeDisplay.RECT rect, uint dpi)
    {
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        return new Rect(
            rect.Left / scale,
            rect.Top / scale,
            (rect.Right - rect.Left) / scale,
            (rect.Bottom - rect.Top) / scale);
    }

    private sealed record DisplayMonitorRegistration(ReactorWindow Window, WindowMessageMonitor Monitor);

    private static class NativeDisplay
    {
        public const int MONITORINFOF_PRIMARY = 0x00000001;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public static uint GetEffectiveDpi(nint hMonitor)
        {
            try
            {
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0)
                    return dpiX;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }
    }
}

/// <summary>Snapshot of a physical display projected into Reactor's DIP-oriented windowing model.</summary>
public sealed record DisplayInfo
{
    /// <summary>Stable monitor id from Win32 <c>MONITORINFOEX.szDevice</c> (for example, <c>\\.\DISPLAY1</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Whether this display is the primary monitor.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Work area in DIPs, converted with this monitor's DPI. On mixed-DPI desktops,
    /// non-primary X/Y values are approximate because Windows exposes virtual-screen
    /// coordinates in physical pixels, not a single global DIP space.
    /// </summary>
    public Rect WorkAreaDip { get; init; }

    /// <summary>
    /// Full monitor bounds in DIPs, converted with this monitor's DPI. On mixed-DPI
    /// desktops, non-primary X/Y values are approximate.
    /// </summary>
    public Rect BoundsDip { get; init; }

    /// <summary>Effective monitor DPI (96 at 100%, 144 at 150%, etc.).</summary>
    public uint Dpi { get; init; }
}
