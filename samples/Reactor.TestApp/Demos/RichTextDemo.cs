// Issue #480 demo — showcases inline UI (sliders, charts, buttons) embedded
// directly inside paragraphs of a single RichTextBlock. The incremental
// RichTextBlock update path means that adjusting a slider only repaints
// the few runs that actually reference its value, and the embedded chart
// controls retain their WinUI identity (no remount churn). With the
// "Highlight reconcile changes" devtools flag on, only the runs and the
// containing RichTextBlock should flash yellow on each interaction — the
// surrounding static prose stays quiet.

using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.Charts;
using static Microsoft.UI.Reactor.Core.Theme;

class RichTextDemo : Component
{
    record Sample(double X, double Y);

    public override Element Render()
    {
        var (sampleCount, setSampleCount) = UseState(32);
        var (amplitude,   setAmplitude)   = UseState(2.0);
        var (frequency,   setFrequency)   = UseState(2.0);
        var (noise,       setNoise)       = UseState(0.2);
        var (seed,        setSeed)        = UseState(1);
        var (highlight,   setHighlight)   = UseState(ReactorFeatureFlags.HighlightReconcileChanges);

        // Memoize the chart data so unrelated state changes (e.g. toggling
        // the highlight flag) don't rebuild the dataset. Changing any of
        // the dependency values rebuilds the data and the three chart
        // controls below will reflect the new shape — but the surrounding
        // text runs that don't reference these values stay untouched.
        var data = UseMemo(
            () => GenerateData(sampleCount, amplitude, frequency, noise, seed),
            sampleCount, amplitude, frequency, noise, seed);

        // Computed summary values referenced by inline text runs below.
        var peak = data.Max(d => d.Y);
        var trough = data.Min(d => d.Y);
        var mean = data.Average(d => d.Y);

        return ScrollViewer(
            VStack(12,
                Heading("Rich Text + Inline UI"),

                TextBlock(
                    "A single RichTextBlock with sliders, charts, and a button " +
                    "embedded inline. Drag any slider to confirm the embedded controls " +
                    "keep their identity (smooth drag, no flicker, no remount) and " +
                    "that text-selection works across paragraph boundaries.")
                    .Foreground(SecondaryText)
                    .TextWrapping(),

                TextBlock(
                    "Reconcile overlay legend — RED = control was mounted (created fresh); " +
                    "YELLOW = control was updated in place. The RichTextBlock itself flashes " +
                    "yellow whenever a Run's text changes (e.g. the runs that display the " +
                    "current slider values) because WinUI's Run/Hyperlink/LineBreak are " +
                    "TextElements, not UIElements, so the overlay can't paint individual " +
                    "runs — only the enclosing block. Embedded UIElements (sliders, charts, " +
                    "buttons) still get their own per-control highlight, and the static " +
                    "narration paragraph at the bottom should never flash.")
                    .Foreground(SecondaryText)
                    .TextWrapping(),

                HStack(8,
                    CheckBox(highlight, v =>
                    {
                        // Toggle both local state (so the checkbox re-renders)
                        // and the global flag (so the reconciler starts/stops
                        // capturing modified elements for the overlay).
                        setHighlight(v);
                        ReactorFeatureFlags.HighlightReconcileChanges = v;
                    }, label: "Highlight reconcile changes"),
                    Button("Re-roll data", () => setSeed(seed + 1))
                ),

                // ─── The actual demo ─────────────────────────────────────
                // One big RichTextBlock — inline sliders, charts, and a
                // button live INSIDE paragraphs, not floating in a stack.
                // Text selection works across paragraphs end-to-end.
                (RichTextBlock(new[]
                {
                    Paragraph(
                        Run("We are sampling a noisy sine wave at "),
                        InlineUI(Slider(sampleCount, 8, 128, v => setSampleCount((int)v))
                            .Width(140)
                            .Set(DisableAncestorScrollPan)),
                        Run($" {sampleCount} points") with { IsBold = true },
                        Run(". The slider thumb stays attached during continuous drag.")),

                    Paragraph(
                        Run("Amplitude "),
                        InlineUI(Slider(amplitude, 0.1, 5.0, v => setAmplitude(Math.Round(v, 2)))
                            .Width(120)
                            .Set(DisableAncestorScrollPan)),
                        Run($" = {amplitude:F2}") with { IsBold = true },
                        Run(", frequency "),
                        InlineUI(Slider(frequency, 0.5, 8.0, v => setFrequency(Math.Round(v, 2)))
                            .Width(120)
                            .Set(DisableAncestorScrollPan)),
                        Run($" = {frequency:F2}") with { IsBold = true },
                        Run(", noise "),
                        InlineUI(Slider(noise, 0.0, 1.5, v => setNoise(Math.Round(v, 2)))
                            .Width(120)
                            .Set(DisableAncestorScrollPan)),
                        Run($" = {noise:F2}") with { IsBold = true },
                        Run(". All three charts below share the same dataset, so " +
                            "changing any slider re-draws each chart — but the static " +
                            "narration in the following paragraphs stays unchanged.")),

                    Paragraph(
                        Run("Here is the raw signal as a line: "),
                        InlineUI(LineChart(data, d => d.X, d => d.Y)
                            .Width(360).Height(120)
                            .Stroke("#4285F4").StrokeWidth(2)),
                        Run($" The peak reaches {peak:F2} and the trough sits at {trough:F2}.")),

                    Paragraph(
                        Run("Binned as bars: "),
                        InlineUI(BarChart(data, d => d.X, d => d.Y)
                            .Width(360).Height(120)
                            .Fill("#34A853")),
                        Run(" — useful when you want to compare discrete buckets " +
                            "rather than read continuous trends.")),

                    Paragraph(
                        Run("Same data again as a filled area: "),
                        InlineUI(AreaChart(data, d => d.X, d => d.Y)
                            .Width(360).Height(120)
                            .Stroke("#EA4335").Fill("#EA4335").FillOpacity(0.25)),
                        Run($" The mean across all {sampleCount} samples is "),
                        Run($"{mean:F3}") with { IsBold = true },
                        Run(".")),

                    Paragraph(
                        Run("Reactor's incremental RichTextBlock reconciler "),
                        Run("preserves the WinUI identity of every embedded control across re-renders. ")
                            with { IsItalic = true },
                        Run("That is what lets the slider thumb above stay smooth while you drag — " +
                            "and lets you press "),
                        InlineUI(Button("this inline button", () => setSeed(seed + 1))),
                        Run(" to re-roll noise without losing any control state. ")),

                    // A purely static paragraph — under the highlight overlay
                    // this paragraph's runs should NEVER flash yellow no matter
                    // how many times you adjust the sliders above.
                    Paragraph(
                        Run("Static narration that never changes: ") with { IsBold = true },
                        Run("Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                            "If the reconciler is doing its job correctly, none of the runs in " +
                            "this paragraph should flash yellow when you interact with any " +
                            "control above — only the runs that actually reference live state " +
                            "should be marked as modified.")),
                }) with { IsTextSelectionEnabled = true })
            )
            .Padding(16)
            .Spacing(12)
        )
        .HorizontalScrollMode(Microsoft.UI.Xaml.Controls.ScrollMode.Disabled)
        .Set(sv =>
        {
            sv.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled;
            sv.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            // NOTE: clicking any inline UI inside a chart-bearing paragraph
            // causes the SV to scroll up by the combined inline-element
            // height. This is the default WinUI behavior, not a Reactor
            // bug — see https://github.com/microsoft/microsoft-ui-reactor
            // (RichTextBlock + inline UI + ScrollViewer issue) for the
            // root cause (ParagraphNode::RemoveEmbeddedElements during
            // re-measure transiently shrinks the RTB and the SV silently
            // clamps VerticalOffset to the new ScrollableHeight).
        });
    }

    // Tells the parent ScrollViewer not to start a pan/zoom manipulation when
    // a touch/mouse gesture originates on this child. Without this, a
    // continuous slider drag was being hijacked by the outer ScrollViewer's
    // vertical pan as soon as the pointer drifted even one pixel vertically,
    // breaking pointer capture on the Thumb mid-drag.
    static void DisableAncestorScrollPan(Microsoft.UI.Xaml.Controls.Slider s)
    {
        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(s,
            Microsoft.UI.Xaml.Controls.ScrollMode.Disabled);
        Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(s,
            Microsoft.UI.Xaml.Controls.ScrollMode.Disabled);
    }

    static Sample[] GenerateData(int count, double amplitude, double frequency, double noise, int seed)
    {
        var rng = new Random(seed * 1000 + count);
        var data = new Sample[count];
        for (int i = 0; i < count; i++)
        {
            double x = i;
            double t = (double)i / Math.Max(1, count - 1);
            double y = amplitude * Math.Sin(2 * Math.PI * frequency * t)
                     + (rng.NextDouble() * 2 - 1) * noise;
            data[i] = new Sample(x, y);
        }
        return data;
    }
}
