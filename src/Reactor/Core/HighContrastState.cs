using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Whether WinUI is in a Windows contrast theme, for <see cref="ThemeRef"/> resolution
/// (issue #1506).
/// </summary>
/// <remarks>
/// <para>The value is what WinUI has applied, as the hosts' theme probes report it
/// (<see cref="Microsoft.UI.Reactor.Hosting.ThemeProbe"/>), not the system setting. Windows
/// announces a contrast switch before WinUI applies it, and a theme dictionary that Reactor reads
/// in that gap gets entries with the wrong theme's values, for the whole app. Following WinUI
/// keeps every read in the theme WinUI is using.</para>
/// <para>Until a probe in the live tree has reported (no host has content in the tree yet), the
/// system setting stands in: <c>SystemParametersInfo(SPI_GETHIGHCONTRAST)</c>, which is cheap,
/// callable from any thread, and free of the WinRT activation that <c>AccessibilitySettings</c>
/// costs. That value is cached until <see cref="Invalidate"/>, which
/// <see cref="ThemeRef.InvalidateResolutionCache"/> calls on every theme or palette change.</para>
/// </remarks>
internal static partial class HighContrastState
{
    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint HCF_HIGHCONTRASTON = 0x00000001;

    private sealed record Snapshot(int Version, bool Value);

    private static int s_version;
    private static Snapshot? s_snapshot;

    // What WinUI has applied, as the probes last reported it (-1: no probe has reported).
    private static int s_applied = -1;
    private static int s_probes;

    // The value resolved brushes were last refreshed for (-1: nothing was resolved yet).
    private static int s_renderedWith = -1;

    /// <summary>
    /// Test seam: replaces the contrast state with this reader's value, cached like the system
    /// setting until <see cref="Invalidate"/>. It wins over what the probes report, so a
    /// selftest can render in a contrast theme without changing the desktop. <c>null</c>
    /// restores the real state.
    /// </summary>
    internal static Func<bool>? ReaderForTest { get; set; }

    /// <summary>
    /// Test seam: replaces only the system setting, which stands in until a probe reports, so a
    /// test can play Windows announcing a switch that WinUI has not applied. <c>null</c>
    /// restores the real read.
    /// </summary>
    internal static Func<bool>? SystemReaderForTest { get; set; }

    /// <summary>
    /// Test seam: replaces what a theme probe reads from WinUI, so a selftest can play WinUI
    /// applying a contrast theme. <c>null</c> restores the real read.
    /// </summary>
    internal static Func<bool>? ProbeReaderForTest { get; set; }

    /// <summary>True when WinUI is in a contrast theme.</summary>
    internal static bool IsActive
    {
        get
        {
            var value = Current();
            // The first value ever used is what the hosts rendered with.
            Interlocked.CompareExchange(ref s_renderedWith, value ? 1 : 0, -1);
            return value;
        }
    }

    private static bool Current()
    {
        var applied = Volatile.Read(ref s_applied);
        if (ReaderForTest is null && applied >= 0)
            return applied == 1;

        var version = Volatile.Read(ref s_version);
        var snapshot = Volatile.Read(ref s_snapshot);
        if (snapshot is not null && snapshot.Version == version)
            return snapshot.Value;

        var value = ReaderForTest is { } reader ? reader()
            : SystemReaderForTest is { } system ? system()
            : TryReadSystem(out var on) && on;
        // Published under the version read above: an Invalidate that lands meanwhile makes
        // the next caller read again instead of serving this value.
        Interlocked.CompareExchange(ref s_snapshot, new Snapshot(version, value), snapshot);
        return value;
    }

    /// <summary>Drops the cached system setting, so the next read without a probe gets it again.</summary>
    internal static void Invalidate() => Interlocked.Increment(ref s_version);

    /// <summary>A theme probe entered the live tree, where WinUI updates it.</summary>
    internal static void ProbeAttached() => Interlocked.Increment(ref s_probes);

    /// <summary>
    /// A theme probe left the live tree, or was removed. With none left in the tree, nothing
    /// reports what WinUI applies, and the system setting stands in again until a probe reports.
    /// </summary>
    internal static void ProbeDetached()
    {
        if (Interlocked.Decrement(ref s_probes) > 0) return;
        Volatile.Write(ref s_applied, -1);
        Invalidate();
    }

    /// <summary>
    /// A theme probe reports what WinUI has applied. When that differs from what resolved
    /// brushes were refreshed for, theme brushes resolve again and every live host fully
    /// re-renders, so resolved <see cref="ThemeRef"/> values (<c>.Resources(...)</c> overrides,
    /// <c>ThemeRef.Resolve</c> in a render) follow, as <c>{ThemeResource}</c> values already do.
    /// Every host reports the same switch; only the first report refreshes, and a first report
    /// before anything was resolved refreshes nothing. Safe on any thread.
    /// </summary>
    internal static void OnApplied(bool highContrast)
    {
        Volatile.Write(ref s_applied, highContrast ? 1 : 0);
        var value = Current() ? 1 : 0;
        int previous;
        do
        {
            previous = Volatile.Read(ref s_renderedWith);
            if (previous == -1 || previous == value) return;
        }
        while (Interlocked.CompareExchange(ref s_renderedWith, value, previous) != previous);
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

    /// <summary>
    /// Test seam: forgets the reported and cached values and what was rendered with. Live
    /// probes report again on their next change.
    /// </summary>
    internal static void ResetForTest()
    {
        Volatile.Write(ref s_applied, -1);
        Volatile.Write(ref s_renderedWith, -1);
        Invalidate();
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
