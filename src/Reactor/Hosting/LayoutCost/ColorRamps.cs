using WColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Compile-time color ramps for the layout-cost meter's top (ms) and bottom
/// (inflation-tail) bars. v1 thresholds are first-principles; re-tune once
/// the overlay is in developers' hands. See spec §Open questions 2.
/// </summary>
/// <remarks>
/// All boundaries are inclusive of the lower bucket (spec uses <c>≤</c>).
/// NaN / negative inputs clamp to the lowest bucket so the meter never draws
/// a garbage color.
/// </remarks>
// TODO: promote to ReactorFeatureFlags if tuning becomes frequent.
internal static class ColorRamps
{
    // Layout-ms ramp
    internal static readonly WColor MsRampGreen  = WColor.FromArgb(255, 0x3C, 0xB0, 0x43);
    internal static readonly WColor MsRampYellow = WColor.FromArgb(255, 0xE0, 0xC4, 0x1A);
    internal static readonly WColor MsRampOrange = WColor.FromArgb(255, 0xE8, 0x84, 0x1A);
    internal static readonly WColor MsRampRed    = WColor.FromArgb(255, 0xDC, 0x2A, 0x2A);

    // Inflation-ratio ramp (applied to the tail portion of the bottom bar)
    internal static readonly WColor InflationGreen  = MsRampGreen;
    internal static readonly WColor InflationYellow = MsRampYellow;
    internal static readonly WColor InflationOrange = MsRampOrange;
    internal static readonly WColor InflationRed    = MsRampRed;

    // "Authored" portion of the bottom bar — neutral gray.
    internal static readonly WColor AuthoredGray = WColor.FromArgb(255, 0xA0, 0xA0, 0xA0);

    // Box chrome
    internal static readonly WColor BoxBackground = WColor.FromArgb(200, 30, 30, 30);
    internal static readonly WColor BoxBorder     = WColor.FromArgb(255, 80, 80, 80);

    public static WColor MsRamp(double layoutMs)
    {
        if (double.IsNaN(layoutMs) || layoutMs <= 2.0) return MsRampGreen;
        if (layoutMs <= 8.0) return MsRampYellow;
        if (layoutMs <= 16.0) return MsRampOrange;
        return MsRampRed;
    }

    public static WColor InflationRamp(double ratio)
    {
        if (double.IsNaN(ratio) || ratio <= 3.0) return InflationGreen;
        if (ratio <= 8.0) return InflationYellow;
        if (ratio <= 20.0) return InflationOrange;
        return InflationRed;
    }
}
