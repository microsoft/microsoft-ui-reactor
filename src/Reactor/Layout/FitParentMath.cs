using System;

namespace Microsoft.UI.Reactor.Layout;

/// <summary>
/// The transient render transform FitParent applies to its host while a resize is pending:
/// scale <see cref="Scale"/> about (<see cref="CenterX"/>, <see cref="CenterY"/>), then translate.
/// </summary>
internal readonly record struct FitParentTransient(
    double CenterX, double CenterY, double Scale, double TranslateX, double TranslateY);

/// <summary>
/// Pure size maths for <see cref="Factories.FitParent"/> (issue #174). No WinUI types, so it is
/// unit-testable headless.
/// </summary>
internal static class FitParentMath
{
    /// <summary>Size changes at or below this many DIPs on both axes are treated as layout jitter.</summary>
    internal const double JitterToleranceDip = 0.5;

    /// <summary>Upper bound for <see cref="FitParentOptions.Debounce"/>.</summary>
    internal static readonly TimeSpan MaxDebounce = TimeSpan.FromSeconds(10);

    /// <summary>True when both axes are finite and strictly positive.</summary>
    internal static bool IsUsableSize(double width, double height) =>
        double.IsFinite(width) && width > 0 && double.IsFinite(height) && height > 0;

    /// <summary>
    /// True when nothing is committed yet (<paramref name="committedW"/> or
    /// <paramref name="committedH"/> &lt;= 0), or when either axis moved by more than
    /// <see cref="JitterToleranceDip"/>.
    /// </summary>
    internal static bool IsSignificantChange(double committedW, double committedH, double newW, double newH)
    {
        if (!(committedW > 0) || !(committedH > 0))
            return true;
        return Math.Abs(newW - committedW) > JitterToleranceDip
            || Math.Abs(newH - committedH) > JitterToleranceDip;
    }

    /// <summary>
    /// The transient that maps content rendered at the committed size onto the new host size:
    /// a uniform <c>min(sx, sy)</c> scale about the content's centre, translated so that centre
    /// lands on the new host centre. FitParent's Canvas layer anchors the content at (0,0), so
    /// the arranged origin is always (0,0). Returns <c>null</c> when either size is unusable.
    /// </summary>
    internal static FitParentTransient? ComputeTransient(double committedW, double committedH, double newW, double newH)
    {
        if (!IsUsableSize(committedW, committedH) || !IsUsableSize(newW, newH))
            return null;

        var s = Math.Min(newW / committedW, newH / committedH);
        var cx = committedW / 2;
        var cy = committedH / 2;
        return new FitParentTransient(cx, cy, s, newW / 2 - cx, newH / 2 - cy);
    }

    /// <summary>
    /// Clamps a debounce to <c>[0, <see cref="MaxDebounce"/>]</c>: non-positive values become
    /// <see cref="TimeSpan.Zero"/> (commit immediately). Never throws.
    /// </summary>
    internal static TimeSpan EffectiveDebounce(TimeSpan debounce)
    {
        if (debounce <= TimeSpan.Zero)
            return TimeSpan.Zero;
        return debounce > MaxDebounce ? MaxDebounce : debounce;
    }
}
