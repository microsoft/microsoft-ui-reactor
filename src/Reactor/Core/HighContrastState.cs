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

    // The setting the hosts were last fully refreshed for (-1: not known yet). Kept apart from
    // the cached value on purpose: a partial re-render (ActualThemeChanged, a palette change)
    // may already resolve with the new setting before the WM_SETTINGCHANGE arrives, and must
    // not stop the full refresh that memoized subtrees still need.
    private static int s_refreshedFor = -1;

    /// <summary>Test seam: replaces the system read; <c>null</c> restores it.</summary>
    internal static Func<bool>? ReaderForTest { get; set; }

    /// <summary>True when a contrast theme is on. Cached until <see cref="Invalidate"/>.</summary>
    internal static bool IsActive
    {
        get
        {
            var version = Volatile.Read(ref s_version);
            var snapshot = Volatile.Read(ref s_snapshot);
            if (snapshot is not null && snapshot.Version == version)
                return snapshot.Value;

            var value = ReaderForTest is { } reader ? reader() : TryReadSystem(out var on) && on;
            // Published under the version read above: an Invalidate that lands meanwhile makes
            // the next caller read again instead of serving this value.
            Interlocked.CompareExchange(ref s_snapshot, new Snapshot(version, value), snapshot);
            // The first value ever used is what the hosts rendered with.
            Interlocked.CompareExchange(ref s_refreshedFor, value ? 1 : 0, -1);
            return value;
        }
    }

    /// <summary>Drops the cached value, so the next <see cref="IsActive"/> reads the system setting.</summary>
    internal static void Invalidate() => Interlocked.Increment(ref s_version);

    /// <summary>
    /// Handles a system notification that the contrast setting may have changed. When it moved
    /// since the last full refresh, theme brushes resolve again and every live host fully
    /// re-renders, so resolved <see cref="ThemeRef"/> values (<c>.Resources(...)</c> overrides,
    /// <c>ThemeRef.Resolve</c> in a render) follow, as <c>{ThemeResource}</c> values already do.
    /// Every window receives the broadcast; only the first call for a change refreshes.
    /// Safe on any thread.
    /// </summary>
    internal static void OnSystemSettingChanged()
    {
        Invalidate();
        var current = IsActive ? 1 : 0;
        if (Interlocked.Exchange(ref s_refreshedFor, current) != current)
            Theme.NotifyResourcesChanged();
    }

    /// <summary>
    /// Reads the system setting. False when the call fails, which the caller treats as no
    /// contrast theme; internal so a test can tell a failing call from "off".
    /// </summary>
    internal static bool TryReadSystem(out bool on)
    {
        var info = new HIGHCONTRASTW { cbSize = (uint)Unsafe.SizeOf<HIGHCONTRASTW>() };
        var ok = SystemParametersInfoW(SPI_GETHIGHCONTRAST, info.cbSize, ref info, 0);
        on = ok && (info.dwFlags & HCF_HIGHCONTRASTON) != 0;
        return ok;
    }

    /// <summary>Test seam: forgets the cached value and the last refreshed setting.</summary>
    internal static void ResetForTest()
    {
        Invalidate();
        Volatile.Write(ref s_refreshedFor, -1);
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
