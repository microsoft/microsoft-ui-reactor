using System.Diagnostics;
using System.Runtime.InteropServices;

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
                Debug.WriteLine($"[Reactor] GetWindowPlacement failed: {Marshal.GetLastWin32Error()}");
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowPlacementCodec.Capture failed: {ex.GetType().Name}: {ex.Message}");
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
                Debug.WriteLine($"[Reactor] WindowPlacementCodec: implausible monitor count {monitorCount}; rejecting payload.");
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
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowPlacementCodec.Restore failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Bounds rectangle of a single display, used in the persistence-fingerprint
/// payload. Values are physical pixels in the virtual-screen coordinate space.
/// </summary>
internal readonly record struct MonitorRect(string? DeviceName, double Left, double Top, double Right, double Bottom);
