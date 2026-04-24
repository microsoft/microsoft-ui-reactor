using WColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Pure geometry/color math that turns a <see cref="ComponentSnapshot"/>
/// into a <see cref="MeterLayout"/>. Kept pure so the math is trivially
/// unit-testable and so the renderer never computes anything non-visual.
/// </summary>
internal static class MeterMath
{
    /// <summary>Spec §Box chrome — one 30 Hz frame.</summary>
    public const double MsBarCeilingMs = 33.0;

    /// <summary>Spec §The two bars — log10 ceiling for the rendered-count axis.</summary>
    public const double CountLogCeiling = 10_001.0;

    public static MeterLayout ComputeLayout(in ComponentSnapshot s, in MeterBox box)
    {
        double layoutMs = double.IsNaN(s.EmaLayoutMs) ? 0 : Math.Max(0, s.EmaLayoutMs);
        float msFrac = (float)Math.Min(layoutMs / MsBarCeilingMs, 1.0);
        float msWidth = box.InnerWidth * msFrac;

        int authored = Math.Max(0, s.AuthoredElementCount);
        int rendered = Math.Max(0, s.RenderedElementCount);

        double authoredFrac = Math.Log10(authored + 1) / Math.Log10(CountLogCeiling);
        double renderedFrac = Math.Log10(rendered + 1) / Math.Log10(CountLogCeiling);
        if (authoredFrac < 0) authoredFrac = 0; else if (authoredFrac > 1) authoredFrac = 1;
        if (renderedFrac < 0) renderedFrac = 0; else if (renderedFrac > 1) renderedFrac = 1;
        double tailFrac = Math.Max(renderedFrac - authoredFrac, 0);

        float authoredBarWidth = (float)(box.InnerWidth * authoredFrac);
        float tailBarX = authoredBarWidth;
        float tailBarWidth = (float)(box.InnerWidth * tailFrac);

        // Clamp sum to inner width to protect against float accumulation drift.
        if (authoredBarWidth + tailBarWidth > box.InnerWidth)
            tailBarWidth = box.InnerWidth - authoredBarWidth;

        double ratio = s.InflationRatio;
        WColor tailColor = ColorRamps.InflationRamp(ratio);

        return new MeterLayout(
            MsBarWidth: msWidth,
            MsBarColor: ColorRamps.MsRamp(layoutMs),
            AuthoredBarWidth: authoredBarWidth,
            AuthoredBarColor: ColorRamps.AuthoredGray,
            TailBarX: tailBarX,
            TailBarWidth: tailBarWidth,
            TailBarColor: tailColor);
    }
}

/// <summary>Inner drawable area of a single meter (box chrome excluded).</summary>
internal readonly record struct MeterBox(float InnerWidth, float BarHeight);

/// <summary>Resolved widths/colors for a single meter, consumed by the Composition renderer.</summary>
internal readonly record struct MeterLayout(
    float MsBarWidth,
    WColor MsBarColor,
    float AuthoredBarWidth,
    WColor AuthoredBarColor,
    float TailBarX,
    float TailBarWidth,
    WColor TailBarColor);
