using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Whether a Windows contrast theme is on, for <see cref="ThemeRef"/> resolution (issue #1506).
/// </summary>
/// <remarks>
/// Read with <c>SystemParametersInfo(SPI_GETHIGHCONTRAST)</c>: cheap, callable from any thread,
/// and free of the WinRT activation that <c>AccessibilitySettings</c> costs (the hosts defer that
/// one until the first chart). The value is cached until <see cref="Invalidate"/>, which
/// <see cref="ThemeRef.InvalidateResolutionCache"/> calls on every theme or palette change.
/// </remarks>
internal static partial class HighContrastState
{
    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint HCF_HIGHCONTRASTON = 0x00000001;

    /// <summary>
    /// <c>SPI_SETHIGHCONTRAST</c>: the <c>wParam</c> of the <c>WM_SETTINGCHANGE</c> that Windows
    /// broadcasts when a contrast theme is turned on or off, or another one is chosen.
    /// </summary>
    internal const nuint SPI_SETHIGHCONTRAST = 0x0043;

    private sealed record Snapshot(int Version, bool Value);

    private static int s_version;
    private static Snapshot? s_snapshot;

    // The value last handed to a resolve (-1: never read), so a change notification can tell
    // whether anything on screen was resolved under the other setting.
    private static int s_lastServed = -1;

    /// <summary>Test seam: replaces the system read; <c>null</c> restores it.</summary>
    internal static Func<bool>? ReaderForTest { get; set; }

    /// <summary>True when a contrast theme is on. Cached until <see cref="Invalidate"/>.</summary>
    internal static bool IsActive
    {
        get
        {
            var version = Volatile.Read(ref s_version);
            var snapshot = Volatile.Read(ref s_snapshot);
            bool value;
            if (snapshot is not null && snapshot.Version == version)
            {
                value = snapshot.Value;
            }
            else
            {
                value = Read();
                // Published under the version read above: an Invalidate that lands meanwhile
                // makes the next caller read again instead of serving this value.
                Interlocked.CompareExchange(ref s_snapshot, new Snapshot(version, value), snapshot);
            }

            var served = value ? 1 : 0;
            if (Volatile.Read(ref s_lastServed) != served)
                Volatile.Write(ref s_lastServed, served);
            return value;
        }
    }

    /// <summary>Drops the cached value, so the next <see cref="IsActive"/> reads the system setting.</summary>
    internal static void Invalidate() => Interlocked.Increment(ref s_version);

    /// <summary>
    /// Handles a system notification that the contrast setting may have changed. When it did,
    /// theme brushes resolve again and every live host fully re-renders, so resolved
    /// <see cref="ThemeRef"/> values (<c>.Resources(...)</c> overrides, <c>ThemeRef.Resolve</c>
    /// in a render) follow, as <c>{ThemeResource}</c> values already do. Safe on any thread.
    /// </summary>
    internal static void OnSystemSettingChanged()
    {
        var previous = Volatile.Read(ref s_lastServed);
        Invalidate();
        var current = IsActive ? 1 : 0;
        if (previous != current)
            Theme.NotifyResourcesChanged();
    }

    /// <summary>Test seam: forgets the cached and the last served value.</summary>
    internal static void ResetForTest()
    {
        Invalidate();
        Volatile.Write(ref s_lastServed, -1);
    }

    private static bool Read()
    {
        if (ReaderForTest is { } reader) return reader();
        var info = new HIGHCONTRASTW { cbSize = (uint)Unsafe.SizeOf<HIGHCONTRASTW>() };
        return SystemParametersInfoW(SPI_GETHIGHCONTRAST, info.cbSize, ref info, 0)
            && (info.dwFlags & HCF_HIGHCONTRASTON) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIGHCONTRASTW
    {
        public uint cbSize;
        public uint dwFlags;
        public nint lpszDefaultScheme;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref HIGHCONTRASTW pvParam, uint fWinIni);
}
