using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Charting.Accessibility;

/// <summary>
/// Charting-side accessibility scanner extension (issue #498). Moved out of the
/// core <see cref="AccessibilityScanner"/> so the core no longer statically
/// references chart-specific types (<see cref="ChartPalette"/>,
/// <see cref="ChartSummarizer"/>, <see cref="D3Color"/>,
/// <see cref="IChartAccessibilityData"/>). Registered with the scanner the first
/// time a chart activates (see <see cref="ChartingRuntime"/>); apps that never
/// render a chart never register it, so the trimmer drops this whole chain.
/// <para>
/// Implements rules A11Y_CHART_001 – A11Y_CHART_012. Chart accessibility metadata
/// is read from the <see cref="ChartA11yData"/> payload that chart elements attach
/// to their realized <see cref="CanvasElement"/>.
/// </para>
/// </summary>
internal sealed class ChartAccessibilityChecker : IScanExtension
{
    internal static readonly ChartAccessibilityChecker Instance = new();

    /// <summary>Runs all chart-specific accessibility rules on chart CanvasElements.</summary>
    public void Check(Element el, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        CanvasElement? canvas = null;

        if (el is CanvasElement c && c.GetAttached<ChartA11yData>() is not null)
        {
            canvas = c;
        }
        else if (el.GetAttached<ChartScannerHint>() is { } scannerHint)
        {
            // FuncElement wrappers (keyboard navigator) carry the inner canvas as a scanner hint
            canvas = scannerHint.InnerCanvas;
        }

        if (canvas is null) return;

        var cd = canvas.GetAttached<ChartA11yData>();
        if (cd is null) return;

        var chartData = cd.Data;

        CheckChartTitle(canvas, chartData, ctx, findings);
        CheckChartDescription(canvas, chartData, ctx, findings);
        CheckChartColorOnly(canvas, cd, ctx, findings);

        // Skip palette checks when .RawColors() opted out — those would produce
        // incorrect warnings for charts that intentionally use custom colors.
        if (!cd.IsRawColors)
        {
            CheckChartPaletteContrast(canvas, cd, ctx, findings);
            CheckChartPaletteColorblind(canvas, cd, ctx, findings);
            CheckChartPaletteBackground(canvas, cd, ctx, findings);
        }
        CheckChartRawColors(canvas, cd, ctx, findings);
        CheckChartInteractiveKeyboard(canvas, cd, ctx, findings);
        CheckChartTightHitTest(canvas, cd, ctx, findings);
        CheckChartFocusIndicatorContrast(canvas, cd, ctx, findings);
        CheckChartAnnounceEveryFrame(canvas, cd, ctx, findings);
    }

    /// <summary>A11Y_CHART_001: Chart has no Title/AutomationName and no derivable name.</summary>
    private static void CheckChartTitle(CanvasElement canvas, IChartAccessibilityData data,
        IScanContext ctx, List<A11yDiagnostic> findings)
    {
        // Has explicit AutomationName? (Ignore "Plot area" — that's an auto-set structural label)
        if (ctx.HasAutomationName(canvas) && canvas.Modifiers?.AutomationName != "Plot area") return;
        // Has Title?
        if (!string.IsNullOrWhiteSpace(data.Name)) return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_001",
            Severity = "warning",
            Message = "Chart has no accessible name — set .Title(\"...\") or .AutomationName(\"...\")",
            WcagCriterion = "1.1.1",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "Title",
                SuggestedValue = null,
                CodeSnippet = ".Title(\"descriptive chart title\")",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>
    /// A11Y_CHART_002: Chart has no explicit <c>Description</c> and no series data,
    /// so the auto-summarizer has nothing to describe.
    /// </summary>
    private static void CheckChartDescription(CanvasElement canvas, IChartAccessibilityData data,
        IScanContext ctx, List<A11yDiagnostic> findings)
    {
        // Has explicit description?
        if (!string.IsNullOrWhiteSpace(data.Description)) return;

        // With any series present the auto-summarizer produces a meaningful
        // description, so only an empty chart (no series) is left unlabeled.
        if (data.Series.Count > 0)
            return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_002",
            Severity = "warning",
            Message = "Chart has no description and no data to auto-summarize — set .Description(\"...\") or provide series data",
            WcagCriterion = "1.1.1",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "Description",
                SuggestedValue = null,
                CodeSnippet = ".Description(\"what this chart shows\")",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>A11Y_CHART_004: .ColorOnly() used — color is the sole series encoding.</summary>
    private static void CheckChartColorOnly(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (!cd.IsColorOnly) return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_004",
            Severity = "warning",
            Message = "Chart uses .ColorOnly() — color is the sole series differentiator, which is inaccessible to colorblind users",
            WcagCriterion = "1.4.1",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "ColorOnly",
                SuggestedValue = "Remove .ColorOnly() or add .SeriesShapes(...)",
                CodeSnippet = "Remove .ColorOnly() to enable default shape+dash encoding",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>A11Y_CHART_009: Custom palette fails pairwise WCAG 3:1 contrast.</summary>
    private static void CheckChartPaletteContrast(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (cd.CustomPalette is not { } palette) return;
        if (palette.Count < 2) return;

        for (int i = 0; i < palette.Count; i++)
        {
            for (int j = i + 1; j < palette.Count; j++)
            {
                double contrast = ChartPalette.ContrastRatio(palette[i], palette[j]);
                if (contrast < 3.0)
                {
                    // Generate hardened alternative
                    var hardenResult = ChartPalette.Harden(
                        Enumerable.Range(0, palette.Count).Select(k => palette[k]).ToArray());

                    findings.Add(new A11yDiagnostic
                    {
                        Id = "A11Y_CHART_009",
                        Severity = "warning",
                        Message = $"Custom palette: colors {i} and {j} have contrast ratio {contrast:F1}:1, below the 3:1 minimum",
                        WcagCriterion = "1.4.11",
                        ElementType = "CanvasElement (Chart)",
                        AutomationId = ctx.GetAutomationId(canvas),
                        ComponentType = ctx.CurrentComponent,
                        Fix = new A11yFixSuggestion
                        {
                            Modifier = cd.CustomPaletteModifier,
                            SuggestedValue = string.Join(", ", hardenResult.Palette.Colors.Select(c => c.ToHex())),
                            CodeSnippet = $".Palette(ChartPalette.OkabeIto) or use .{cd.CustomPaletteModifier}(...) with the suggested values",
                        },
                        Context = ctx.BuildContext(canvas),
                    });
                    return; // Report first failure only
                }
            }
        }
    }

    /// <summary>A11Y_CHART_010: Custom palette fails colorblind ΔE &lt; 10.</summary>
    private static void CheckChartPaletteColorblind(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (cd.CustomPalette is not { } palette) return;
        if (palette.Count < 2) return;

        for (int i = 0; i < palette.Count; i++)
        {
            for (int j = i + 1; j < palette.Count; j++)
            {
                double minDE = ChartPalette.MinColorblindDeltaE(palette[i], palette[j]);
                if (minDE < 10.0)
                {
                    var hardenResult = ChartPalette.Harden(
                        Enumerable.Range(0, palette.Count).Select(k => palette[k]).ToArray());

                    findings.Add(new A11yDiagnostic
                    {
                        Id = "A11Y_CHART_010",
                        Severity = "warning",
                        Message = $"Custom palette: colors {i} and {j} have ΔE {minDE:F1} under colorblind simulation, below the 10.0 minimum",
                        WcagCriterion = "1.4.1",
                        ElementType = "CanvasElement (Chart)",
                        AutomationId = ctx.GetAutomationId(canvas),
                        ComponentType = ctx.CurrentComponent,
                        Fix = new A11yFixSuggestion
                        {
                            Modifier = cd.CustomPaletteModifier,
                            SuggestedValue = string.Join(", ", hardenResult.Palette.Colors.Select(c => c.ToHex())),
                            CodeSnippet = ".Palette(ChartPalette.OkabeIto) or use hardened alternative",
                        },
                        Context = ctx.BuildContext(canvas),
                    });
                    return;
                }
            }
        }
    }

    /// <summary>
    /// A11Y_CHART_011: Custom palette fails contrast against the chart background.
    /// When the author declares a representative background via <c>.ChartBackground(...)</c>,
    /// the check is scoped to that single active background and emitted as a <c>warning</c>
    /// (the palette is only penalized for a background it actually renders on). Otherwise the
    /// scanner is theme-agnostic and cannot know the active theme, so it flags failure against
    /// <c>either</c> fixed background as an <c>info</c> finding to avoid alert fatigue (issue #633).
    /// </summary>
    private static void CheckChartPaletteBackground(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (cd.CustomPalette is not { } palette) return;

        if (cd.ChartBackground is { } activeBg)
        {
            CheckChartPaletteAgainstKnownBackground(canvas, cd, palette, activeBg, ctx, findings);
            return;
        }

        var lightBg = new D3Color(255, 255, 255);
        var darkBg = new D3Color(32, 32, 32);

        for (int i = 0; i < palette.Count; i++)
        {
            double lightContrast = ChartPalette.ContrastRatio(palette[i], lightBg);
            double darkContrast = ChartPalette.ContrastRatio(palette[i], darkBg);

            bool failsLight = lightContrast < 3.0;
            bool failsDark = darkContrast < 3.0;

            // A theme-agnostic chart palette must render legibly under whichever
            // background is active, so a color is flagged when it fails 3:1 contrast
            // against *either* the light or dark background (per spec 026). Requiring
            // failure against both is mathematically unreachable: a color light enough
            // to fail vs white can never also be dark enough to fail vs near-black.
            //
            // The scanner is a static, theme-agnostic tree walk (issue #498) and has
            // no access to the chart's effective rendered background, so it cannot
            // know which theme is actually active. A color flagged here only fails if
            // the chart renders on the matching background — hence this is emitted as
            // an informational finding, not a warning, to avoid alert fatigue on
            // palettes that are fine for the theme they actually run under. Authors can
            // declare .ChartBackground(...) to scope this to the active background and
            // promote it to a warning (issue #633).
            if (failsLight || failsDark)
            {
                var hardenResult = ChartPalette.Harden(
                    Enumerable.Range(0, palette.Count).Select(k => palette[k]).ToArray());

                // failsLight and failsDark are mutually exclusive: a color light enough
                // to fail vs white can never also be dark enough to fail vs near-black.
                string failedBackground = failsLight
                    ? $"a light ({lightContrast:F1}:1) background"
                    : $"a dark ({darkContrast:F1}:1) background";

                findings.Add(new A11yDiagnostic
                {
                    Id = "A11Y_CHART_011",
                    Severity = "info",
                    Message = $"Custom palette: color {i} would fail 3:1 contrast if rendered on {failedBackground}",
                    WcagCriterion = "1.4.11",
                    ElementType = "CanvasElement (Chart)",
                    AutomationId = ctx.GetAutomationId(canvas),
                    ComponentType = ctx.CurrentComponent,
                    Fix = new A11yFixSuggestion
                    {
                        Modifier = cd.CustomPaletteModifier,
                        SuggestedValue = string.Join(", ", hardenResult.Palette.Colors.Select(c => c.ToHex())),
                        CodeSnippet = "Adjust color lightness to ensure ≥3:1 contrast against chart backgrounds",
                    },
                    Context = ctx.BuildContext(canvas),
                });
                return;
            }
        }
    }

    /// <summary>
    /// A11Y_CHART_011 scoped to a single author-declared background (issue #633): each palette
    /// color is checked against that one active background, the false positives that forced the
    /// info downgrade are gone, so a real failure is emitted as a <c>warning</c>. The fix
    /// suggestion hardens the palette toward that same background so it is a genuine remediation.
    /// </summary>
    private static void CheckChartPaletteAgainstKnownBackground(
        CanvasElement canvas, ChartA11yData cd, ChartPalette palette, D3Color activeBg,
        IScanContext ctx, List<A11yDiagnostic> findings)
    {
        for (int i = 0; i < palette.Count; i++)
        {
            double contrast = ChartPalette.ContrastRatio(palette[i], activeBg);
            if (contrast >= 3.0) continue;

            var original = Enumerable.Range(0, palette.Count).Select(k => palette[k]).ToArray();
            var hardened = ChartPalette.Harden(original, new HardenOptions { Background = activeBg }).Palette;

            // Only offer the hardened palette as a concrete fix when it ACTUALLY changed
            // AND every color now clears 3:1 against the active background. The pairwise
            // distinguishability guard can legitimately leave a color un-nudged; echoing the
            // unchanged (still-failing) palette back as the "fix" would reintroduce the
            // bad-fix-suggestion defect of #628/#629 (issue #633 M1). Fall back to a textual
            // instruction when we cannot prove the suggestion is real.
            bool changed = Enumerable.Range(0, Math.Min(hardened.Count, original.Length))
                .Any(k => !string.Equals(hardened[k].ToHex(), original[k].ToHex(), StringComparison.OrdinalIgnoreCase));
            bool allPass = hardened.Count == original.Length
                && Enumerable.Range(0, hardened.Count).All(k => ChartPalette.ContrastRatio(hardened[k], activeBg) >= 3.0);
            string suggestedValue = changed && allPass
                ? string.Join(", ", hardened.Colors.Select(c => c.ToHex()))
                : $"Adjust palette colors to ≥3:1 contrast against {activeBg.ToHex()}";

            findings.Add(new A11yDiagnostic
            {
                Id = "A11Y_CHART_011",
                Severity = "warning",
                Message = $"Custom palette: color {i} fails 3:1 contrast ({contrast:F1}:1) against the chart background {activeBg.ToHex()}",
                WcagCriterion = "1.4.11",
                ElementType = "CanvasElement (Chart)",
                AutomationId = ctx.GetAutomationId(canvas),
                ComponentType = ctx.CurrentComponent,
                Fix = new A11yFixSuggestion
                {
                    Modifier = cd.CustomPaletteModifier,
                    SuggestedValue = suggestedValue,
                    CodeSnippet = "Adjust color lightness to ensure ≥3:1 contrast against the chart background",
                },
                Context = ctx.BuildContext(canvas),
            });
            return;
        }
    }

    /// <summary>A11Y_CHART_012: .RawColors() escape hatch used — informational.</summary>
    private static void CheckChartRawColors(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (!cd.IsRawColors) return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_012",
            Severity = "info",
            Message = "Chart uses .RawColors() — palette accessibility checks are bypassed",
            WcagCriterion = "1.4.1",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "RawColors",
                SuggestedValue = null,
                CodeSnippet = "Consider using .Palette(ChartPalette.OkabeIto) or .SeriesColors() for accessible colors",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>A11Y_CHART_003: Chart is .Interactive() but keyboard navigation is disabled.</summary>
    private static void CheckChartInteractiveKeyboard(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (!cd.IsInteractive) return;
        if (!cd.IsKeyboardDisabled) return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_003",
            Severity = "warning",
            Message = "Interactive chart has keyboard navigation disabled — users who rely on keyboard cannot navigate data points",
            WcagCriterion = "2.1.1",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "DisableKeyboard",
                SuggestedValue = null,
                CodeSnippet = "Remove .DisableKeyboard() to enable keyboard navigation",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>A11Y_CHART_005: .TightHitTest() on markers that may be smaller than 24px.</summary>
    private static void CheckChartTightHitTest(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (!cd.IsTightHitTest) return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_005",
            Severity = "warning",
            Message = "Chart uses .TightHitTest() — point markers may have hit targets smaller than 24×24 CSS pixels",
            WcagCriterion = "2.5.8",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "TightHitTest",
                SuggestedValue = null,
                CodeSnippet = "Remove .TightHitTest() to enable automatic 24×24 hit target expansion",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>A11Y_CHART_006: Custom focus indicator color has insufficient contrast (&lt; 3:1) against chart background.</summary>
    private static void CheckChartFocusIndicatorContrast(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (cd.CustomFocusColor is not { } focusColor) return;

        // Check contrast against both light and dark chart backgrounds
        var fc = new D3Color(focusColor.R, focusColor.G, focusColor.B);
        var lightBg = new D3Color(255, 255, 255);
        var darkBg = new D3Color(30, 30, 30);

        double lightContrast = ChartPalette.ContrastRatio(fc, lightBg);
        double darkContrast = ChartPalette.ContrastRatio(fc, darkBg);

        // Fail if the custom color doesn't meet 3:1 against either background
        if (lightContrast >= 3.0 && darkContrast >= 3.0)
            return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_006",
            Severity = "warning",
            Message = $"Custom focus indicator color has contrast ratio {Math.Min(lightContrast, darkContrast):F1}:1 — minimum 3:1 required (WCAG 2.4.13). Use the default double-ring focus indicator.",
            WcagCriterion = "2.4.13",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "FocusColor",
                SuggestedValue = null,
                CodeSnippet = "Remove .FocusColor(...) to use the default double-ring focus indicator",
            },
            Context = ctx.BuildContext(canvas),
        });
    }

    /// <summary>A11Y_CHART_007: .AnnounceEveryFrame() floods the live region with rapid-fire announcements.</summary>
    private static void CheckChartAnnounceEveryFrame(CanvasElement canvas, ChartA11yData cd, IScanContext ctx, List<A11yDiagnostic> findings)
    {
        if (!cd.IsAnnounceEveryFrame) return;

        findings.Add(new A11yDiagnostic
        {
            Id = "A11Y_CHART_007",
            Severity = "warning",
            Message = ".AnnounceEveryFrame() causes the chart to announce every animation frame — this floods the live region and overwhelms screen-reader users",
            WcagCriterion = "4.1.3",
            ElementType = "CanvasElement (Chart)",
            AutomationId = ctx.GetAutomationId(canvas),
            ComponentType = ctx.CurrentComponent,
            Fix = new A11yFixSuggestion
            {
                Modifier = "AnnounceEveryFrame",
                SuggestedValue = null,
                CodeSnippet = "Remove .AnnounceEveryFrame() — the chart's live region already debounces announcements to settled states",
            },
            Context = ctx.BuildContext(canvas),
        });
    }
}
