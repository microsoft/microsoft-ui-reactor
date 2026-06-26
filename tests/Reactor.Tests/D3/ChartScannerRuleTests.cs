using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.D3;

public class ChartScannerRuleTests
{
    static ChartScannerRuleTests()
    {
        // The chart accessibility rules now live in the Charting subsystem and are
        // contributed to the core scanner via registration (issue #498). Install
        // the checker so these unit tests exercise the chart rules.
        AccessibilityScanner.RegisterScanExtension(ChartAccessibilityChecker.Instance);
    }

    private record DataPoint(double X, double Y);

    private static readonly DataPoint[] SampleData =
        Enumerable.Range(0, 5).Select(i => new DataPoint(i, (i + 1) * 10.0)).ToArray();

    /// <summary>
    /// Creates a CanvasElement that simulates a chart with the given chart data properties.
    /// Avoids calling ToElement() which requires WinUI COM initialization.
    /// </summary>
    private static CanvasElement MakeChartCanvas(
        IChartAccessibilityData? chartData = null,
        bool isColorOnly = false,
        bool isRawColors = false,
        ChartPalette? customPalette = null,
        string? automationName = null,
        bool isInteractive = false,
        bool isKeyboardDisabled = false,
        bool isTightHitTest = false,
        global::Windows.UI.Color? customFocusColor = null,
        bool isAnnounceEveryFrame = false,
        D3Color? chartBackground = null)
    {
        var canvas = new CanvasElement([])
        {
            Width = 400,
            Height = 300,
        };

        if (chartData != null)
        {
            canvas = (CanvasElement)canvas.SetAttached(new ChartA11yData(chartData)
            {
                IsColorOnly = isColorOnly,
                IsRawColors = isRawColors,
                CustomPalette = customPalette,
                IsInteractive = isInteractive,
                IsKeyboardDisabled = isKeyboardDisabled,
                IsTightHitTest = isTightHitTest,
                CustomFocusColor = customFocusColor,
                IsAnnounceEveryFrame = isAnnounceEveryFrame,
                ChartBackground = chartBackground,
            });
        }

        if (automationName != null)
            canvas = (CanvasElement)(canvas as Element).AutomationName(automationName);

        return canvas;
    }

    /// <summary>Mock chart accessibility data for testing.</summary>
    private sealed class MockChartData : IChartAccessibilityData
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public IReadOnlyList<ChartSeriesDescriptor> Series { get; init; } = [];
        public IReadOnlyList<ChartAxisDescriptor> Axes { get; init; } = [];
        public ChartViewport? Viewport { get; init; }
        public string ChartTypeName { get; init; } = "Line";
    }

    private static MockChartData DataWithSeries(string? name = null, string? description = null, int pointCount = 5)
    {
        var points = Enumerable.Range(0, pointCount)
            .Select(i => new ChartPointDescriptor(i.ToString(), (i + 1) * 10.0))
            .ToArray();
        return new MockChartData
        {
            Name = name,
            Description = description,
            Series = [new ChartSeriesDescriptor("Series 1", points)],
            Axes = [
                new ChartAxisDescriptor(ChartAxisType.X, "X", 0, pointCount - 1),
                new ChartAxisDescriptor(ChartAxisType.Y, "Y", 10, pointCount * 10),
            ],
        };
    }

    // ── A11Y_CHART_001: Chart has no Title/AutomationName ───────────

    [Fact]
    public void A11Y_CHART_001_ChartWithoutTitle_Flagged()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries());
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_001");
    }

    [Fact]
    public void A11Y_CHART_001_ChartWithTitle_Passes()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue Over Time"));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_001");
    }

    [Fact]
    public void A11Y_CHART_001_ChartWithAutomationName_Passes()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries(), automationName: "Revenue Chart");
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_001");
    }

    // ── A11Y_CHART_002: Chart has no Description ────────────────────

    [Fact]
    public void A11Y_CHART_002_ChartWithData_HasAutoSummary_Passes()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_002");
    }

    [Fact]
    public void A11Y_CHART_002_EmptyChartWithoutDescription_Flagged()
    {
        var canvas = MakeChartCanvas(chartData: new MockChartData());
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_002");
    }

    [Fact]
    public void A11Y_CHART_002_EmptyChartWithDescription_Passes()
    {
        var canvas = MakeChartCanvas(chartData: new MockChartData
        {
            Description = "Revenue chart showing monthly income trends.",
        });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_002");
    }

    // ── A11Y_CHART_004: ColorOnly ───────────────────────────────────

    [Fact]
    public void A11Y_CHART_004_ColorOnly_Flagged()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), isColorOnly: true);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_004");
    }

    [Fact]
    public void A11Y_CHART_004_DefaultEncoding_Passes()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_004");
    }

    // ── A11Y_CHART_009: Custom palette fails pairwise contrast ──────

    [Fact]
    public void A11Y_CHART_009_LowContrastPalette_Flagged()
    {
        var palette = ChartPalette.FromColors(
            new D3Color(128, 128, 128),
            new D3Color(135, 135, 135)); // Very similar grays
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_009");

        var finding = findings.First(f => f.Id == "A11Y_CHART_009");
        Assert.NotNull(finding.Fix.SuggestedValue);
    }

    [Fact]
    public void A11Y_CHART_009_HighContrastPalette_Passes()
    {
        var palette = ChartPalette.FromColors(
            new D3Color(0, 0, 0),
            new D3Color(255, 255, 255)); // Maximum contrast
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_009");
    }

    // ── A11Y_CHART_010: Colorblind ΔE check ────────────────────────

    [Fact]
    public void A11Y_CHART_010_ColorblindUnsafePalette_Processed()
    {
        // Two near-identical colors have a colorblind ΔE well under the 10.0
        // minimum, so the rule must fire. Asserting the specific rule ID (rather
        // than just NotNull(findings)) ensures the rule can't be silently deleted.
        var palette = ChartPalette.FromColors(
            new D3Color(100, 100, 100),
            new D3Color(101, 100, 100));
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_010");
    }

    // ── A11Y_CHART_011: Background contrast ─────────────────────────

    [Fact]
    public void A11Y_CHART_011_VeryLightColor_FailsLightBackground()
    {
        // Near-white yellow has ~1:1 contrast against the light (255,255,255)
        // background, so it would fail the 3:1 minimum if rendered on a light theme
        // and must be flagged. Asserting the specific rule ID guards against the rule
        // silently becoming unreachable again (see issue #628). The finding is
        // informational, not a warning: the scanner is theme-agnostic and cannot know
        // which background the chart actually renders on (avoids alert fatigue).
        var palette = ChartPalette.FromColors(new D3Color(255, 255, 200));
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("info", finding.Severity);
    }

    [Fact]
    public void A11Y_CHART_011_VeryDarkColor_FailsDarkBackground()
    {
        // Near-black color has ~1:1 contrast against the dark (32,32,32)
        // background, so it fails on the dark theme and must be flagged.
        var palette = ChartPalette.FromColors(new D3Color(28, 28, 28));
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_011");
    }

    [Fact]
    public void A11Y_CHART_011_MidToneColor_PassesBothBackgrounds()
    {
        // A mid-tone gray keeps ≥3:1 contrast against both the light and dark
        // backgrounds, so the rule should not fire.
        var palette = ChartPalette.FromColors(new D3Color(128, 128, 128));
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_011");
    }

    // ── A11Y_CHART_011: active-background scoping (issue #633) ───────

    [Fact]
    public void A11Y_CHART_011_KnownLightBackground_FailingColor_FiresWarning()
    {
        // Near-white yellow fails 3:1 against the light (255,255,255) background. When the
        // author declares .ChartBackground(light), the scanner knows the palette actually
        // renders on that background, so the failure is real and is promoted from info to a
        // WARNING (the false positives that forced the info downgrade are gone — issue #633).
        var palette = ChartPalette.FromColors(new D3Color(255, 255, 200));
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customPalette: palette,
            chartBackground: new D3Color(255, 255, 255));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void A11Y_CHART_011_KnownDarkBackground_LightColor_DoesNotFire()
    {
        // The SAME near-white palette passes 3:1 against the dark (32,32,32) background. With
        // the background scoped to dark, the color is legible on the background it actually
        // renders on, so the rule must NOT fire — a palette is only penalized for a background
        // it will actually render on (issue #633). Under the old both-backgrounds OR behavior
        // this same palette would have produced an info finding.
        var palette = ChartPalette.FromColors(new D3Color(255, 255, 200));
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customPalette: palette,
            chartBackground: new D3Color(32, 32, 32));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_011");
    }

    [Fact]
    public void A11Y_CHART_011_NoDeclaredBackground_KeepsInfoBothBackgroundsBehavior()
    {
        // Without a declared background the scanner stays theme-agnostic: it flags the
        // near-white color against EITHER fixed background and keeps the INFO severity so the
        // alert-fatigue mitigation from #629 is preserved for charts that don't opt in. Pinning
        // the severity guards the unknown-background path from silently regressing (issue #633).
        var palette = ChartPalette.FromColors(new D3Color(255, 255, 200));
        var canvas = MakeChartCanvas(chartData: DataWithSeries(name: "Revenue"), customPalette: palette);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("info", finding.Severity);
    }

    [Fact]
    public void A11Y_CHART_011_KnownDarkBackground_FailingColor_FiresWarningWithRealFix()
    {
        // A near-black series fails 3:1 against the dark #202020 background it actually renders
        // on, so the scanner fires a WARNING (issue #633 M4). Critically, the suggested fix must
        // be a REAL remediation — colors that genuinely clear 3:1 against that dark background —
        // not an echo of the still-failing near-black color. Darkening can never satisfy a
        // near-black background, so a direction rule that always darkens would echo a failing
        // color (the #628/#629 bad-fix-suggestion defect class, #633 M2). This assertion fails
        // against the pre-M2 darken-only Harden, so it is the load-bearing guard.
        var darkBg = new D3Color(32, 32, 32);
        var palette = ChartPalette.FromColors(new D3Color(28, 28, 28));
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customPalette: palette,
            chartBackground: darkBg);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);

        // The fix's suggested value must parse to a palette whose every color clears 3:1
        // against the active dark background (M1 + M2 together: a verified, non-echo fix).
        var fixedColors = finding.Fix!.SuggestedValue!
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(D3Color.Parse)
            .ToArray();
        Assert.NotEmpty(fixedColors);
        Assert.All(fixedColors, c => Assert.True(
            ChartPalette.ContrastRatio(c, darkBg) >= 3.0,
            $"Suggested color {c.ToHex()} still fails 3:1 against dark background {darkBg.ToHex()}"));
    }

    [Fact]
    public void A11Y_CHART_011_ChartBackgroundDslModifier_FlowsIntoScan()
    {
        // Drives the actual .ChartBackground(...) DSL modifier (string overload) through the
        // real AttachChartData wiring into ChartA11yData, then scans — pinning the modifier→
        // attach path itself rather than setting ChartA11yData.ChartBackground directly via
        // MakeChartCanvas (issue #633 L1; also exercises the L2 string→D3Color overload). We
        // attach to a bare CanvasElement to stay headless: the real D3Canvas builds a
        // SolidColorBrush and would need WinUI COM.
        var chart = Charts.LineChart(Array.Empty<DataPoint>(), d => d.X, d => d.Y)
            .SeriesColors(new D3Color(255, 255, 200)) // near-white: fails the light background
            .ChartBackground("#FFFFFF");              // string overload → light (255,255,255)
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void ChartBackground_NormalizesToOpaqueRgb()
    {
        // Contrast math (ChartPalette.ContrastRatio/RelativeLuminance) ignores opacity — a
        // semi-transparent background can't be evaluated without knowing what's behind it. So
        // .ChartBackground(...) must drop alpha and store opaque RGB regardless of overload,
        // rather than persisting a misleading alpha (PR #638 review). Drive a translucent
        // Windows.UI.Color through the real modifier and assert the attached value is opaque.
        var translucent = global::Windows.UI.Color.FromArgb(0x80, 0x20, 0x20, 0x20);
        var chart = Charts.LineChart(Array.Empty<DataPoint>(), d => d.X, d => d.Y)
            .ChartBackground(translucent);
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });

        // Assert.IsType narrows the nullable ChartBackground to a non-null D3Color in one step,
        // so the subsequent reads don't dereference a nullable value type (CodeQL r3461264777).
        var bg = Assert.IsType<D3Color>(canvas.GetAttached<ChartA11yData>()!.ChartBackground);
        Assert.Equal(1.0, bg.Opacity);
        Assert.Equal(0x20, bg.R);
        Assert.Equal(0x20, bg.G);
        Assert.Equal(0x20, bg.B);
    }

    [Fact]
    public void A11Y_CHART_011_PieChart_ChartBackgroundDslModifier_FlowsIntoScan()
    {
        // H1: PieChartElement<T> has its OWN .ChartBackground(...) overloads, _chartBackground
        // field, and AttachChartData path — separate from ChartElement<T>. Pin that the Pie
        // modifier wiring also scopes A11Y_CHART_011 to the declared background and promotes it
        // to a warning (issue #633 / PR #638 review). A near-white palette fails the declared
        // light background. Headless: attach to a bare CanvasElement to skip the D3Canvas/
        // SolidColorBrush WinUI COM path, exactly as the ChartElement<T> DSL test does.
        var palette = ChartPalette.FromColors(new D3Color(255, 255, 200));
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .Palette(palette)
            .ChartBackground("#FFFFFF");
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);
    }

    // ── A11Y_CHART_011: PieChart .SetColors() is scanner-visible (issue #645) ───────

    [Fact]
    public void A11Y_CHART_011_PieChart_SetColors_DeclaredBackground_FiresWarning()
    {
        // Issue #645 (red→green): PieChartElement<T>.SetColors(...) feeds _colorPalette, the
        // colors the slices are ACTUALLY drawn with. Before the fix those colors never reached
        // ChartA11yData.CustomPalette, so A11Y_CHART_011 silently never ran on them — a
        // .SetColors(low).ChartBackground(low) chart looked contrast-checked but wasn't. Now the
        // rendered .SetColors palette is the scanner's source of truth, so a near-white slice
        // color that fails the declared light background fires the same WARNING that the
        // .Palette(...) path does. This assertion FAILS against the pre-#645 code (zero findings),
        // so it is the load-bearing guard for the fix.
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(255, 255, 200)) // near-white: fails the light background
            .ChartBackground("#FFFFFF");
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);
        // H1 (issue #645): the machine-consumable fix names the modifier a PIE exposes — .SetColors(),
        // not .SeriesColors() — via ChartA11yData.CustomPaletteModifier (the checker is chart-type-aware).
        Assert.Equal("SetColors", finding.Fix.Modifier);
    }

    [Fact]
    public void A11Y_CHART_011_PieChart_SetColors_NoBackground_FiresInfo()
    {
        // .SetColors colors also flow through the theme-agnostic (no declared background) path
        // exactly like a .Palette(...) palette: the same near-white color fails against EITHER
        // fixed light/dark background and is reported as INFO. Pins that the unification covers
        // the unknown-background arm too, not just the scoped-background warning (issue #645).
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(255, 255, 200));
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("info", finding.Severity);
    }

    [Fact]
    public void A11Y_CHART_011_PieChart_SetColors_PassesDeclaredBackground_DoesNotFire()
    {
        // The same near-white .SetColors color PASSES 3:1 against the declared dark (#202020)
        // background it actually renders on, so the rule must NOT fire — a palette is only
        // penalized for the background it renders on. Mirrors the .Palette(...) dark-background
        // no-fire case, proving .SetColors gets identical treatment (issue #645).
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(255, 255, 200))
            .ChartBackground("#202020");
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_011");
    }

    [Fact]
    public void A11Y_CHART_011_PieChart_BothSet_RenderedSetColorsWins_FiresOnSetColors()
    {
        // Both-set semantics (issue #645): when .SetColors(...) AND .Palette(...) are both set, the
        // pie still RENDERS the .SetColors colors (_colorPalette), so the scanner validates THOSE —
        // keeping the scanner and the rendered output in lockstep. Here the rendered .SetColors
        // color is near-white (fails the declared white background) while the .Palette() color is a
        // mid-tone gray that PASSES white. The rule fires on the rendered near-white color. If
        // _palette wrongly won, the scanner would see the passing gray and emit nothing — so this
        // single-finding assertion pins that the rendered .SetColors palette is the source of truth.
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(255, 255, 200))                 // rendered: fails white
            .Palette(ChartPalette.FromColors(new D3Color(128, 128, 128))) // advisory: passes white
            .ChartBackground("#FFFFFF");
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void A11Y_CHART_011_PieChart_BothSet_PaletteIgnoredWhenSetColorsPasses_DoesNotFire()
    {
        // Inverse of the both-set test: the rendered .SetColors color is a mid-tone gray that
        // PASSES the declared white background, while the advisory .Palette() color is near-white
        // and would FAIL white. Because only the rendered .SetColors palette is scanned, the rule
        // must NOT fire. If _palette wrongly won, the near-white .Palette color would trip a
        // warning — so this no-finding assertion pins that .Palette() is ignored for the scanner
        // whenever .SetColors() is set (issue #645).
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(128, 128, 128))                 // rendered: passes white
            .Palette(ChartPalette.FromColors(new D3Color(255, 255, 200))) // advisory: fails white
            .ChartBackground("#FFFFFF");
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_011");
    }

    // ── .SetColors() is checked across A11Y_CHART_009/010/011 like .Palette() (issue #645) ──

    [Fact]
    public void A11Y_CHART_009_PieChart_SetColors_MultiColor_FiresPairwise_WithSetColorsFix()
    {
        // Issue #645 acceptance: .SetColors() is contrast-checked across 009/010/011 "exactly like"
        // .Palette() — not just the 011 background rule. Two near-identical grays fail the pairwise
        // ≥3:1 rule (A11Y_CHART_009). Pins that a MULTI-color .SetColors palette (Count≥2) reaches the
        // pairwise rule via the pie path, and that the machine-consumable fix names the modifier a PIE
        // exposes — .SetColors(), not .SeriesColors() (the checker is now chart-type-aware).
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(128, 128, 128), new D3Color(135, 135, 135)); // similar grays: <3:1 pairwise
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_009");
        Assert.Equal("SetColors", finding.Fix.Modifier);
    }

    [Fact]
    public void A11Y_CHART_010_PieChart_SetColors_MultiColor_FiresColorblind_WithSetColorsFix()
    {
        // Companion to the 009 test: two colors whose colorblind ΔE is under the 10.0 minimum trip
        // A11Y_CHART_010 via the pie .SetColors() path. Reuses the proven colorblind-unsafe pair from
        // the series-chart 010 test. Again the fix names .SetColors() — the pie's real modifier —
        // confirming chart-type-aware remediation across the colorblind rule too (issue #645).
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .SetColors(new D3Color(100, 100, 100), new D3Color(101, 100, 100)); // ΔE < 10 under CVD sim
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_010");
        Assert.Equal("SetColors", finding.Fix.Modifier);
    }

    [Fact]
    public void A11Y_CHART_011_PieChart_EmptySetColors_FallsBackToPaletteForScanner()
    {
        // M4: ScannerPalette's `{ Count: > 0 }` guard. Calling .SetColors() with NO args clears
        // _colorPalette (the pie reverts to its default render palette), so the scanner falls back to
        // the .Palette() palette — preserving the pre-#645 scanner-visible behavior. Pins that an empty
        // .SetColors() does NOT suppress the .Palette() contrast finding: the near-white .Palette color
        // still fails the declared white background and fires the warning (issue #645).
        var chart = Charts.PieChart(Array.Empty<DataPoint>(), d => d.Y)
            .Palette(ChartPalette.FromColors(new D3Color(255, 255, 200))) // near-white: fails white
            .SetColors()                                                  // cleared → ScannerPalette falls back to _palette
            .ChartBackground("#FFFFFF");
        var canvas = chart.AttachChartDataForTest(new CanvasElement([]) { Width = 400, Height = 300 });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void A11Y_CHART_011_KnownBackground_PairwiseGuardBlocksFix_FallsBackToTextualHint()
    {
        // M2: when the contrast-improving nudge would collide with another series, the pairwise-
        // distinguishability guard skips it, so Harden cannot produce a fully-passing palette. The
        // fix must then fall back to a TEXTUAL instruction rather than echoing the still-failing
        // palette as a "fix" (the #628/#629 bad-suggestion defect class this whole arc closes).
        //
        // Provable construction: on the dark #202020 background, color0 #2b2b2b (near-black) fails
        // 3:1 and can only clear it by LIGHTENING. color1 #888888 already passes #202020, so the
        // background pass never moves it. But any color light enough to clear #202020 (relative
        // luminance ≳ 0.14, gray ≈ 111) sits within 3:1 of color1 (luminance ≈ 0.25); the only
        // escape — lightening all the way past color1 to ≈ gray 237 — requires crossing a band
        // (gray ≈ 66..237) where every incremental candidate is pairwise-blocked. color0 is
        // therefore trapped failing the background, so no real hardened palette exists and the
        // suggestion must be the textual fallback.
        var bg = new D3Color(32, 32, 32);
        var palette = ChartPalette.FromColors(new D3Color(0x2b, 0x2b, 0x2b), new D3Color(0x88, 0x88, 0x88));
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customPalette: palette,
            chartBackground: bg);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = Assert.Single(findings, f => f.Id == "A11Y_CHART_011");

        // Detection/severity stay correct: a real failure against the declared background → warning.
        Assert.Equal("warning", finding.Severity);

        // The fix is NOT an echo: because Harden could not prove a fully-passing palette under the
        // pairwise guard, the suggestion is the TEXTUAL fallback arm (starts with the instruction
        // phrase), never the hex-list arm (string.Join of palette colors). It may name the active
        // background hex (#202020), but it must not echo the still-failing palette color #2b2b2b.
        Assert.NotNull(finding.Fix);
        var suggested = finding.Fix!.SuggestedValue ?? "";
        Assert.StartsWith("Adjust palette colors", suggested);
        Assert.DoesNotContain("2b2b2b", suggested.ToLowerInvariant());
    }

    [Fact]
    public void A11Y_CHART_012_RawColors_EmittedAsInfo()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isRawColors: true);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var rawFinding = findings.FirstOrDefault(f => f.Id == "A11Y_CHART_012");
        Assert.NotNull(rawFinding);
        Assert.Equal("info", rawFinding!.Severity);
    }

    [Fact]
    public void A11Y_CHART_012_NormalPalette_NotEmitted()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customPalette: ChartPalette.OkabeIto);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_012");
    }

    // ── Scanner skips chart rules for non-chart elements ────────────

    [Fact]
    public void Scanner_NonChartCanvas_NoChartRules()
    {
        var tree = VStack(Canvas(TextBlock("Hello")));

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id.StartsWith("A11Y_CHART_"));
    }

    // ── Clean chart ─────────────────────────────────────────────────

    [Fact]
    public void Scanner_CleanChart_ZeroChartViolations()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries(
            name: "Monthly Revenue",
            description: "Shows revenue growth from January to May"));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var chartFindings = findings.Where(f => f.Id.StartsWith("A11Y_CHART_")).ToList();
        Assert.Empty(chartFindings);
    }

    // ── Fix suggestion structure ────────────────────────────────────

    [Fact]
    public void FixSuggestion_A11Y_CHART_001_HasCorrectStructure()
    {
        var canvas = MakeChartCanvas(chartData: DataWithSeries());
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        var finding = findings.First(f => f.Id == "A11Y_CHART_001");

        Assert.Equal("Title", finding.Fix.Modifier);
        Assert.Contains(".Title(", finding.Fix.CodeSnippet);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal("1.1.1", finding.WcagCriterion);
    }

    // ── Pie chart scanner rules ─────────────────────────────────────

    [Fact]
    public void Scanner_PieChartWithoutTitle_Flagged()
    {
        var canvas = MakeChartCanvas(chartData: new MockChartData
        {
            ChartTypeName = "Pie",
            Series = [new ChartSeriesDescriptor("Slices", [
                new ChartPointDescriptor("A", 30),
                new ChartPointDescriptor("B", 70)])],
        });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_001");
    }

    [Fact]
    public void Scanner_PieChartWithTitle_Passes()
    {
        var canvas = MakeChartCanvas(chartData: new MockChartData
        {
            Name = "Market Share",
            ChartTypeName = "Pie",
            Series = [new ChartSeriesDescriptor("Slices", [
                new ChartPointDescriptor("A", 30),
                new ChartPointDescriptor("B", 70)])],
        });
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_001");
    }

    // ── A11Y_CHART_003: Interactive chart with keyboard disabled ──────

    [Fact]
    public void A11Y_CHART_003_InteractiveKeyboardDisabled_Flagged()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isInteractive: true,
            isKeyboardDisabled: true);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_003");
    }

    [Fact]
    public void A11Y_CHART_003_InteractiveKeyboardEnabled_Passes()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isInteractive: true,
            isKeyboardDisabled: false);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_003");
    }

    [Fact]
    public void A11Y_CHART_003_NonInteractiveKeyboardDisabled_Passes()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isInteractive: false,
            isKeyboardDisabled: true);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_003");
    }

    // ── A11Y_CHART_005: TightHitTest ──────────────────────────────────

    [Fact]
    public void A11Y_CHART_005_TightHitTest_Flagged()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isTightHitTest: true);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_005");
    }

    [Fact]
    public void A11Y_CHART_005_NoTightHitTest_Passes()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isTightHitTest: false);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_005");
    }

    // ── Scanner skips chart rules for non-chart elements ──────────────

    [Fact]
    public void Scanner_NonChartCanvas_SkipsChartRules()
    {
        var canvas = new CanvasElement([]) { Width = 100, Height = 100 };
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id.StartsWith("A11Y_CHART_"));
    }

    // ── A11Y_CHART_006: Focus indicator contrast ────────────────────

    [Fact]
    public void A11Y_CHART_006_LowContrastFocusColor_Flagged()
    {
        // Very light gray fails 3:1 contrast against white background (~1.7:1)
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customFocusColor: global::Windows.UI.Color.FromArgb(255, 200, 200, 200));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_006");
        var diag = findings.First(f => f.Id == "A11Y_CHART_006");
        Assert.Equal("2.4.13", diag.WcagCriterion);
        Assert.Equal("FocusColor", diag.Fix.Modifier);
    }

    [Fact]
    public void A11Y_CHART_006_HighContrastFocusColor_Passes()
    {
        // Bright red has high contrast against both backgrounds
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            customFocusColor: global::Windows.UI.Color.FromArgb(255, 255, 0, 0));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_006");
    }

    [Fact]
    public void A11Y_CHART_006_NoCustomFocusColor_Passes()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"));
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_006");
    }

    // ── A11Y_CHART_007: AnnounceEveryFrame floods live region ───────

    [Fact]
    public void A11Y_CHART_007_AnnounceEveryFrame_Flagged()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isAnnounceEveryFrame: true);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_007");
        var diag = findings.First(f => f.Id == "A11Y_CHART_007");
        Assert.Equal("4.1.3", diag.WcagCriterion);
        Assert.Equal("AnnounceEveryFrame", diag.Fix.Modifier);
    }

    [Fact]
    public void A11Y_CHART_007_NoAnnounceEveryFrame_Passes()
    {
        var canvas = MakeChartCanvas(
            chartData: DataWithSeries(name: "Revenue"),
            isAnnounceEveryFrame: false);
        var tree = VStack(canvas);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_007");
    }
}
