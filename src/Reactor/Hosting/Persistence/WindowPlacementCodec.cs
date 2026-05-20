using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Serializes and deserializes the persistence payload: monitor-layout
/// fingerprint followed by a <c>WINDOWPLACEMENT</c> struct. Layout matches
/// <c>WinUIEx.WindowManager.LoadPersistence</c> so existing on-disk data is
/// forward-compatible. (spec 036 §8)
/// </summary>
/// <remarks>
/// Format (binary):
/// <code>
///   int32   monitorCount
///   for each monitor:
///     length-prefixed string  monitor.DeviceName  (informational, ignored on read)
///     double  rect.Left
///     double  rect.Top
///     double  rect.Right
///     double  rect.Bottom
///   byte[sizeof(WINDOWPLACEMENT)]  placement
/// </code>
/// The monitor-name string is not part of the fingerprint check — display
/// names can drift (locale changes, USB hubs) without the layout actually
/// changing. We compare bounds only.
/// </remarks>
internal static class WindowPlacementCodec
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(nint hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

    private const int SW_NORMAL = 1;
    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_MAXIMIZE = 3;
    private const int WPF_RESTORETOMAXIMIZED = 0x0002;

    /// <summary>
    /// Capture the current placement of <paramref name="hwnd"/> together with
    /// the current monitor layout fingerprint, returning a byte payload safe
    /// to hand to <see cref="IWindowPersistenceStore.Write"/>.
    /// </summary>
    internal static byte[]? Capture(nint hwnd, IReadOnlyList<MonitorRect> monitors)
    {
        try
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref placement))
            {
                // GetLastError on a Win32 BOOL failure: surface the raw code
                // through the typed HResultFailed channel so apps capturing
                // Microsoft-UI-Reactor see why placement save was skipped.
                DiagnosticLog.HResultFailed(
                    LogCategory.Persistence,
                    "WindowPlacementCodec.GetWindowPlacement",
                    Marshal.GetLastWin32Error());
                return null;
            }

            using var ms = new global::System.IO.MemoryStream();
            using var bw = new global::System.IO.BinaryWriter(ms);
            bw.Write(monitors.Count);
            foreach (var m in monitors)
            {
                bw.Write(m.DeviceName ?? string.Empty);
                bw.Write((double)m.Left);
                bw.Write((double)m.Top);
                bw.Write((double)m.Right);
                bw.Write((double)m.Bottom);
            }

            int structSize = Marshal.SizeOf<WINDOWPLACEMENT>();
            var buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(placement, buffer, false);
                var bytes = new byte[structSize];
                Marshal.Copy(buffer, bytes, 0, structSize);
                bw.Write(bytes);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            bw.Flush();
            return ms.ToArray();
        }
        catch (global::System.IO.IOException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "WindowPlacementCodec.Capture", ex);
            return null;
        }
    }

    /// <summary>
    /// Restore placement onto <paramref name="hwnd"/> if the embedded
    /// fingerprint still matches the current monitor layout. Returns
    /// <c>false</c> when the fingerprint mismatches or the payload is
    /// malformed (caller falls back to spec default placement).
    /// </summary>
    internal static bool Restore(nint hwnd, byte[] data, IReadOnlyList<MonitorRect> currentMonitors)
    {
        try
        {
            using var ms = new global::System.IO.MemoryStream(data, writable: false);
            using var br = new global::System.IO.BinaryReader(ms);

            int monitorCount = br.ReadInt32();
            if (monitorCount < 0 || monitorCount > 64)
            {
                if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Persistence))
                    ReactorEventSource.Log.PersistenceRejected("placement", "implausible-monitor-count");
                return false;
            }
            if (monitorCount != currentMonitors.Count)
                return false;

            for (int i = 0; i < monitorCount; i++)
            {
                _ = br.ReadString(); // skip name — not part of fingerprint
                double l = br.ReadDouble();
                double t = br.ReadDouble();
                double r = br.ReadDouble();
                double b = br.ReadDouble();
                var cur = currentMonitors[i];
                if (cur.Left != l || cur.Top != t || cur.Right != r || cur.Bottom != b)
                    return false;
            }

            int structSize = Marshal.SizeOf<WINDOWPLACEMENT>();
            var bytes = br.ReadBytes(structSize);
            if (bytes.Length != structSize) return false;

            var buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.Copy(bytes, 0, buffer, structSize);
                var placement = Marshal.PtrToStructure<WINDOWPLACEMENT>(buffer);
                placement.length = structSize;

                // W-5 hardening: sanity-bound the decoded placement before we
                // hand it to SetWindowPlacement. Same-IL adversaries (or just
                // a corrupted save from an earlier crash) could plant negative
                // widths, oversized rects, or window-state values outside the
                // SW_* enum. The shell does some clamping of its own, but
                // rejecting implausible payloads here means we fall through to
                // the framework default placement instead of asking the OS to
                // best-effort-fix our junk.
                if (!IsPlausiblePlacement(placement))
                {
                    // PII: do NOT emit the raw rect / showCmd on the ETW
                    // payload — those can encode user behavior signals
                    // (multi-mon layout fingerprinting). A short reason
                    // label is enough to triage the reject in a trace.
                    if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Persistence))
                        ReactorEventSource.Log.PersistenceRejected("placement", "implausible-rect");
                    return false;
                }

                // Match WinUIEx semantics: a window saved minimized that was
                // previously maximized restores to maximized; otherwise force
                // SW_NORMAL so we never come back as a stuck minimized icon.
                if (placement.showCmd == SW_SHOWMINIMIZED && (placement.flags & WPF_RESTORETOMAXIMIZED) == WPF_RESTORETOMAXIMIZED)
                    placement.showCmd = SW_MAXIMIZE;
                else if (placement.showCmd != SW_MAXIMIZE)
                    placement.showCmd = SW_NORMAL;

                return SetWindowPlacement(hwnd, ref placement);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (global::System.IO.EndOfStreamException)
        {
            // Tampered payload truncated mid-record. Treat as schema mismatch
            // rather than an "exception swallow" — no value in the type-only
            // ETW payload because the type IS the reason.
            if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Persistence))
                ReactorEventSource.Log.PersistenceRejected("placement", "truncated");
            return false;
        }
        catch (global::System.IO.IOException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "WindowPlacementCodec.Restore", ex);
            return false;
        }
    }

    /// <summary>
    /// Sanity-bounds for a decoded <see cref="WINDOWPLACEMENT"/>. Reject
    /// payloads that almost certainly came from corruption or tampering before
    /// they reach <c>SetWindowPlacement</c>. Specifically:
    /// <list type="bullet">
    ///   <item><description><c>showCmd</c> must be one of the SW_* values we
    ///   know how to round-trip (NORMAL, MINIMIZED, MAXIMIZE).</description></item>
    ///   <item><description><c>rcNormalPosition</c> must have positive width
    ///   and height not exceeding <c>MaxPlausibleDimensionPx</c>; the rect
    ///   itself must lie within the virtual-screen sanity box.</description></item>
    /// </list>
    /// Bounds are deliberately loose — we want to accept legitimate setups
    /// (large multi-monitor rigs, negative coordinates from secondary monitors
    /// to the left of the primary) and only reject obvious garbage.
    /// </summary>
    /// <remarks>W-5 hardening; threat model 2026-05-08.</remarks>
    internal static bool IsPlausiblePlacement(WINDOWPLACEMENT p)
    {
        // Cover the SW_* values Capture writes plus SW_RESTORE / SW_SHOW so a
        // hand-edited file using documented constants still loads.
        if (p.showCmd != SW_NORMAL && p.showCmd != SW_SHOWMINIMIZED && p.showCmd != SW_MAXIMIZE
            && p.showCmd != 5 /* SW_SHOW */ && p.showCmd != 9 /* SW_RESTORE */)
            return false;

        var r = p.rcNormalPosition;
        long width = (long)r.Right - r.Left;
        long height = (long)r.Bottom - r.Top;
        if (width <= 0 || height <= 0) return false;
        if (width > MaxPlausibleDimensionPx || height > MaxPlausibleDimensionPx) return false;

        if (!IsPlausibleCoordinate(r.Left) || !IsPlausibleCoordinate(r.Top) ||
            !IsPlausibleCoordinate(r.Right) || !IsPlausibleCoordinate(r.Bottom))
            return false;

        // Min/max position points are advisory; only sanity-check ordinate
        // ranges so a bogus value can't push the window into oblivion.
        if (!IsPlausibleCoordinate(p.ptMinPosition.X) || !IsPlausibleCoordinate(p.ptMinPosition.Y) ||
            !IsPlausibleCoordinate(p.ptMaxPosition.X) || !IsPlausibleCoordinate(p.ptMaxPosition.Y))
            return false;

        return true;
    }

    /// <summary>
    /// Largest single-axis virtual-screen extent we'll accept on restore.
    /// 32768 covers any plausible multi-monitor setup; values past that almost
    /// certainly indicate corruption or tampering.
    /// </summary>
    internal const int MaxPlausibleDimensionPx = 32768;

    /// <summary>
    /// Largest absolute coordinate magnitude we'll accept on restore. Negative
    /// values are legal (monitors to the left of / above the primary), but
    /// values past 65536 in either direction are not how Windows lays out
    /// real displays.
    /// </summary>
    internal const int MaxPlausibleCoordinateMagnitude = 65536;

    private static bool IsPlausibleCoordinate(int v)
        => v >= -MaxPlausibleCoordinateMagnitude && v <= MaxPlausibleCoordinateMagnitude;
}

/// <summary>
/// Bounds rectangle of a single display, used in the persistence-fingerprint
/// payload. Values are physical pixels in the virtual-screen coordinate space.
/// </summary>
internal readonly record struct MonitorRect(string? DeviceName, double Left, double Top, double Right, double Bottom);
