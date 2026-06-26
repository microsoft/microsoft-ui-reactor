namespace Microsoft.UI.Reactor.Charting.Accessibility;

/// <summary>
/// Abstraction that chart elements implement to expose their data to
/// <see cref="ChartAutomationPeer"/> without coupling the peer to any
/// concrete chart type.
/// </summary>
internal interface IChartAccessibilityData
{
    string? Name { get; }
    string? Description { get; }
    IReadOnlyList<ChartSeriesDescriptor> Series { get; }
    IReadOnlyList<ChartAxisDescriptor> Axes { get; }
    ChartViewport? Viewport { get; }

    /// <summary>
    /// Human-readable chart type name for auto-generated accessible names
    /// (e.g., "Line", "Bar", "Pie", "Tree", "Force graph").
    /// </summary>
    string ChartTypeName => "Chart";
}

// ═══════════════════════════════════════════════════════════════════
//  Descriptor records — immutable snapshots of chart structure
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Describes a single data point in a chart series for accessibility purposes.
/// </summary>
/// <param name="XLabel">Human-readable x-axis label (e.g., "March 14").</param>
/// <param name="YValue">Numeric y value.</param>
/// <param name="FormattedLabel">
/// Pre-formatted label for the point (e.g., "$42,300 on March 14").
/// When null, the peer generates a default label.
/// </param>
public record ChartPointDescriptor(
    string XLabel,
    double YValue,
    string? FormattedLabel = null);

/// <summary>
/// Describes a single series in a chart.
/// </summary>
/// <param name="Name">Human-readable series name (e.g., "Revenue").</param>
/// <param name="Points">Ordered data points in this series.</param>
public record ChartSeriesDescriptor(
    string Name,
    IReadOnlyList<ChartPointDescriptor> Points);

/// <summary>
/// Describes a chart axis (x or y).
/// </summary>
/// <param name="AxisType">Which axis this descriptor represents.</param>
/// <param name="Label">Human-readable axis label (e.g., "Month").</param>
/// <param name="Min">Minimum visible value.</param>
/// <param name="Max">Maximum visible value.</param>
/// <param name="Units">Unit annotation (e.g., "USD", "°C").</param>
public record ChartAxisDescriptor(
    ChartAxisType AxisType,
    string? Label,
    double Min,
    double Max,
    string? Units = null);

/// <summary>
/// Describes the current visible viewport (for pan/zoom scenarios).
/// </summary>
/// <param name="XMin">Left edge of the visible range.</param>
/// <param name="XMax">Right edge of the visible range.</param>
/// <param name="YMin">Bottom edge of the visible range.</param>
/// <param name="YMax">Top edge of the visible range.</param>
public record ChartViewport(
    double XMin,
    double XMax,
    double YMin,
    double YMax);

/// <summary>Identifies which axis a descriptor represents.</summary>
public enum ChartAxisType
{
    X,
    Y,
}

/// <summary>
/// Attached to wrapper elements (e.g., <see cref="Core.FuncElement"/> from
/// keyboard-navigator wrapping) so the accessibility scanner can inspect the
/// inner chart canvas without needing to evaluate the render function.
/// </summary>
internal record ChartScannerHint(Core.CanvasElement InnerCanvas);

/// <summary>
/// Accessibility metadata a chart element attaches to its realized
/// <see cref="Core.CanvasElement"/> (via the element <c>Attached</c> dictionary)
/// so the accessibility scanner's chart rules can inspect it. Carried out-of-band
/// in the attached dictionary — rather than as typed slots on
/// <see cref="Core.CanvasElement"/> — so the core never statically references any
/// Charting type (issue #498). Read back by
/// <see cref="ChartAccessibilityChecker"/>.
/// </summary>
internal sealed record ChartA11yData(IChartAccessibilityData Data)
{
    /// <summary>Chart used <c>.ColorOnly()</c> — scanner flags as A11Y_CHART_004.</summary>
    public bool IsColorOnly { get; init; }

    /// <summary>Chart used <c>.RawColors()</c> — scanner flags as A11Y_CHART_012 and skips palette checks.</summary>
    public bool IsRawColors { get; init; }

    /// <summary>Chart is interactive with keyboard navigation enabled.</summary>
    public bool IsInteractive { get; init; }

    /// <summary>Keyboard navigation explicitly disabled — scanner flags as A11Y_CHART_003.</summary>
    public bool IsKeyboardDisabled { get; init; }

    /// <summary>Hit targets not expanded to 24×24 — scanner flags as A11Y_CHART_005.</summary>
    public bool IsTightHitTest { get; init; }

    /// <summary>Chart announces every animation frame — scanner flags as A11Y_CHART_007.</summary>
    public bool IsAnnounceEveryFrame { get; init; }

    /// <summary>Custom palette set on the chart, if any — scanner validates for contrast.</summary>
    public ChartPalette? CustomPalette { get; init; }

    /// <summary>
    /// DSL modifier the scanner names in its palette-fix suggestions (A11Y_CHART_009/010/011) for
    /// this chart — e.g. <c>"SeriesColors"</c> for series charts, <c>"SetColors"</c> for pie charts
    /// (issue #645). The fix-suggestion modifier is machine-consumable (an agent applies the named
    /// method), so it must name a modifier the chart actually exposes: a pie has <c>.SetColors(...)</c>,
    /// not <c>.SeriesColors(...)</c>. Defaults to <c>"SeriesColors"</c>; pie charts override it.
    /// </summary>
    public string CustomPaletteModifier { get; init; } = "SeriesColors";

    /// <summary>
    /// Whether this chart's <c>.Palette(...)</c> is advisory-only and does NOT drive the rendered
    /// colors — <c>true</c> for pie charts, where <c>.SetColors(...)</c> (or the Category10 default)
    /// is what renders and <c>.Palette(...)</c> is merely a scanner fallback (issue #645). When
    /// <c>true</c>, the palette-contrast fix suggestions (A11Y_CHART_009/010) must NOT offer
    /// <c>.Palette(ChartPalette.OkabeIto)</c> as a remediation — applying it would not change what
    /// the chart actually draws — and instead point only at <see cref="CustomPaletteModifier"/>
    /// (or removing it to fall back to the vetted default palette). Defaults to <c>false</c> (series
    /// charts, whose <c>.Palette(...)</c> drives rendering); pie charts override it.
    /// </summary>
    public bool IsPaletteAdvisoryOnly { get; init; }

    /// <summary>Custom focus indicator color, if any — scanner validates 3:1 contrast (A11Y_CHART_006).</summary>
    public global::Windows.UI.Color? CustomFocusColor { get; init; }

    /// <summary>
    /// Author-declared representative background the chart actually renders on, if any
    /// (set via <c>.ChartBackground(...)</c>). When present, the theme-agnostic scanner
    /// can scope A11Y_CHART_011's palette contrast check to this single active background
    /// (promoting it to a <c>warning</c>) instead of flagging failure against <c>either</c>
    /// fixed background (<c>info</c>). Left null for theme-agnostic charts that may render
    /// on any background.
    /// </summary>
    public D3.D3Color? ChartBackground { get; init; }
}
