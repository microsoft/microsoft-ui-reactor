namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.6 / §2.25 — multi-display clamp for restored floating
//  window bounds.
//
//  Pure math used by `DockFloatingWindow.Open` when a saved (x, y, w, h)
//  needs to be honored. Lives separately so the §8.10 reliability
//  selftest can exercise the algorithm with synthetic `DisplayArea`
//  rectangles instead of requiring a real multi-monitor rig.
//
//  Algorithm:
//   1. If the saved bounds intersect any display by at least the minimum
//      visible area (200 × 100 DIPs), keep them — that's enough chrome
//      for the user to grab and reposition.
//   2. Otherwise, recenter on the *primary* display (first display in the
//      list, matching `DisplayArea.Primary` enumeration order). The
//      window size is preserved unless it exceeds the primary; then we
//      clamp to the primary work area minus a 32-DIP margin.
//
//  The 200 × 100 visibility floor matches Windows' own off-screen
//  restore heuristic (the same threshold WinUI's `AppWindow` uses to
//  decide whether to honor a saved Position).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A display rectangle in screen-coords (DIPs). Mirrors the subset of
/// <c>DisplayArea</c> we need for headless tests.
/// </summary>
public readonly record struct DockDisplay(double X, double Y, double Width, double Height)
{
    /// <summary>The right edge of the display (X + Width).</summary>
    public double Right => X + Width;
    /// <summary>The bottom edge of the display (Y + Height).</summary>
    public double Bottom => Y + Height;
}

/// <summary>
/// A restored floating-window bounds tuple.
/// </summary>
public readonly record struct DockFloatingBounds(double X, double Y, double Width, double Height);

/// <summary>
/// Pure clamp math for spec §2.6 / §2.25 multi-display restore.
/// </summary>
public static class DockFloatingClamp
{
    /// <summary>Minimum on-screen overlap (DIPs) required to keep saved bounds.</summary>
    public const double MinVisibleWidth = 200;
    /// <summary>Minimum on-screen overlap (DIPs) required to keep saved bounds.</summary>
    public const double MinVisibleHeight = 100;
    /// <summary>Margin (DIPs) used when recentering an oversize window.</summary>
    public const double PrimaryClampMargin = 32;

    /// <summary>
    /// Clamps saved floating-window bounds against the set of available
    /// displays. Returns the original bounds when sufficiently visible;
    /// otherwise recenters on the first display in <paramref name="displays"/>
    /// (the primary by convention) with size preserved or clamped.
    /// </summary>
    /// <remarks>
    /// When <paramref name="displays"/> is empty (no displays — unusual,
    /// e.g. a headless harness with no <c>DisplayArea</c>) returns the
    /// saved bounds unchanged.
    /// </remarks>
    public static DockFloatingBounds Clamp(DockFloatingBounds saved, IReadOnlyList<DockDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);

        if (displays.Count == 0) return saved;

        if (IsSufficientlyVisible(saved, displays)) return saved;

        // Recenter on the primary display. Clamp the size if it doesn't
        // fit; preserve it otherwise.
        var primary = displays[0];
        var w = saved.Width;
        var h = saved.Height;
        var maxW = primary.Width - (PrimaryClampMargin * 2);
        var maxH = primary.Height - (PrimaryClampMargin * 2);
        if (maxW < MinVisibleWidth) maxW = primary.Width;
        if (maxH < MinVisibleHeight) maxH = primary.Height;
        if (w > maxW) w = maxW;
        if (h > maxH) h = maxH;
        if (w <= 0) w = MinVisibleWidth;
        if (h <= 0) h = MinVisibleHeight;

        var x = primary.X + ((primary.Width - w) / 2);
        var y = primary.Y + ((primary.Height - h) / 2);
        return new DockFloatingBounds(x, y, w, h);
    }

    /// <summary>
    /// Returns true when the saved bounds overlap at least one display
    /// by the minimum visible area (200 × 100 DIPs).
    /// </summary>
    public static bool IsSufficientlyVisible(DockFloatingBounds saved, IReadOnlyList<DockDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);

        var savedRight = saved.X + saved.Width;
        var savedBottom = saved.Y + saved.Height;

        foreach (var d in displays)
        {
            var ix = Math.Max(saved.X, d.X);
            var iy = Math.Max(saved.Y, d.Y);
            var iw = Math.Min(savedRight, d.Right) - ix;
            var ih = Math.Min(savedBottom, d.Bottom) - iy;
            if (iw >= MinVisibleWidth && ih >= MinVisibleHeight) return true;
        }
        return false;
    }
}
