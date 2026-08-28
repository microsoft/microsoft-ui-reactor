using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace SourceMapExplorer;

/// <summary>
/// Spec 010 — an interactive proof of per-element source mapping.
///
/// <para>Click anything in the left panel. The inspector on the right reports
/// the control you hit and the exact <c>file:line</c> of the DSL call that
/// created it. Nothing in the sample UI carries a callback or a key — these are
/// plain display leaves, the ones the reconciler deliberately does NOT tag
/// (PR #468). They are addressable only because the source-map stamp puts them
/// back on the map.</para>
///
/// <para>The "Source mapping" toggle flips
/// <see cref="ReactorSourceMap.Enabled"/> live. Turn it off and re-click: the
/// same elements report nothing, because the interceptors skip the stamp and
/// the reconciler stops tagging them. That is the runtime gate devtools
/// controls.</para>
/// </summary>
internal sealed class App : Component
{
    public override Element Render()
    {
        var (hit, setHit) = UseState<string?>(null);
        var (scan, setScan) = UseState<string?>(null);
        var (mapping, setMapping) = UseState(ReactorSourceMap.Enabled);

        // Bumped whenever the flag flips, and used as the panel's key so the whole
        // subtree genuinely remounts. Without it the toggle looks half-broken: the
        // flag only governs NEW stamps, and an unchanged TextBlock takes the
        // reconciler's shallow-skip path, keeping the ReactorState (and the
        // location) it was mounted with. That is correct — the element really did
        // come from that line — but it makes "off" read as "8 of 14 mapped"
        // instead of "0 of 14", which obscures what the gate does.
        var (generation, setGeneration) = UseState(0);

        // Stable across renders so the scan button can reach the realized panel.
        var canvasRef = UseMemo(() => new ElementRef(), []);

        void Describe(UIElement target, Action<string?> sink)
        {
            // <snippet:read-source-location>
            SourceLocation? src = ReactorSourceMap.GetSource(target);
            string label = src is null
                ? "(no source location)"
                : $"{src.Value.ToShortString()}";   // e.g. "MainPage.cs:34"
            // </snippet:read-source-location>

            sink(src is null
                ? $"{target.GetType().Name}\n{label}"
                : $"{target.GetType().Name}\n{label}\n\n{src.Value.FilePath}");
        }

        // The single inspection primitive: given a point in host coordinates, find
        // the innermost element under it that carries a source location. Shared by
        // the pointer handler and the "Inspect deepest leaf" button so both drive
        // identical code.
        UIElement? InspectAtPoint(global::Windows.Foundation.Point point, UIElement root)
        {
            foreach (var candidate in VisualTreeHelper.FindElementsInHostCoordinates(point, root))
            {
                if (ReactorSourceMap.GetSource(candidate) is not null) return candidate;
            }
            return null;
        }

        // Hit-test from the panel root rather than putting a handler on each leaf:
        // a leaf with a callback would be tagged anyway, which would prove nothing.
        void OnPanelPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is not UIElement root) return;
            var found = InspectAtPoint(e.GetCurrentPoint(null).Position, root);
            if (found is null) { setHit("(nothing under the pointer carries a source location)"); return; }
            Describe(found, setHit);
        }

        // Round-trips the inspection without needing a mouse: walk to the deepest
        // mapped leaf, compute its own centre in host coordinates, then hit-test
        // that point. If the hit-test resolves to a different location than the
        // walk reported for the same control, the two disagree and the output says
        // so — which is the assertion, not the "no crash".
        void OnInspectDeepest()
        {
            if (canvasRef.Current is not { } root) { setHit("(panel not realized yet)"); return; }

            UIElement? deepest = null;
            var deepestDepth = -1;

            void Walk(DependencyObject node, int depth)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is FrameworkElement fe
                        && ReactorSourceMap.GetSource(fe) is not null
                        && depth > deepestDepth)
                    {
                        deepest = fe;
                        deepestDepth = depth;
                    }
                    Walk(child, depth + 1);
                }
            }

            Walk(root, 0);

            if (deepest is not FrameworkElement target)
            {
                setHit("(no mapped element found — is source mapping on?)");
                return;
            }

            var walkSrc = ReactorSourceMap.GetSource(target);
            var centre = target.TransformToVisual(null)
                .TransformPoint(new global::Windows.Foundation.Point(target.ActualWidth / 2, target.ActualHeight / 2));
            var viaHitTest = InspectAtPoint(centre, root);
            var hitSrc = viaHitTest is null ? null : ReactorSourceMap.GetSource(viaHitTest);

            var agree = hitSrc is not null && walkSrc is not null && hitSrc.Value == walkSrc.Value;
            setHit(
                $"{target.GetType().Name}\n{walkSrc?.ToShortString() ?? "-"}\n\n" +
                $"hit-test at its own centre ({centre.X:F0},{centre.Y:F0})\n" +
                $"  -> {viaHitTest?.GetType().Name ?? "nothing"}  {hitSrc?.ToShortString() ?? "-"}\n" +
                $"  {(agree ? "AGREES with the tree walk" : "DISAGREES with the tree walk")}");
        }

        void OnScan()
        {
            if (canvasRef.Current is not { } root)
            {
                setScan("(panel not realized yet)");
                return;
            }

            var lines = new List<string>();
            var mapped = 0;
            var total = 0;

            void Walk(DependencyObject node, int depth)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is UIElement ui)
                    {
                        total++;
                        var src = ReactorSourceMap.GetSource(ui);
                        if (src is not null) mapped++;
                        lines.Add($"{new string(' ', depth * 2)}{ui.GetType().Name}  {(src?.ToShortString() ?? "-")}");
                    }
                    Walk(child, depth + 1);
                }
            }

            Walk(root, 0);
            setScan($"{mapped} of {total} controls mapped\n\n{string.Join("\n", lines)}");
        }

        return VStack(
            Heading("Reactor source map explorer"),
            TextBlock("Click anything on the left. Every element below is a plain display leaf — no callbacks, no keys.")
                .Foreground(Theme.SecondaryText),

            HStack(
                Button(mapping ? "Source mapping: ON" : "Source mapping: OFF", () =>
                {
                    ReactorSourceMap.Enabled = !mapping;
                    setMapping(!mapping);
                    setGeneration(generation + 1);
                    setHit(null);
                    setScan(null);
                }),
                Button("Scan visual tree", OnScan),
                Button("Inspect deepest leaf", OnInspectDeepest)
            ).Spacing(8),

            HStack(
                // ── The sample UI under inspection ──────────────────────────
                Border(
                    VStack(
                        Heading("Order summary"),
                        TextBlock("Two items, ready to ship."),
                        Border(
                            VStack(
                                TextBlock("Wireless keyboard"),
                                TextBlock("$79.00").Bold()
                            ).Spacing(2)
                        ).Padding(10).Background(Theme.SubtleFill),
                        Border(
                            VStack(
                                TextBlock("USB-C cable"),
                                TextBlock("$12.00").Bold()
                            ).Spacing(2)
                        ).Padding(10).Background(Theme.SubtleFill),
                        HStack(
                            TextBlock("Total").Bold(),
                            TextBlock("$91.00").Bold()
                        ).Spacing(12)
                    ).Spacing(10)
                )
                .Padding(16)
                .Width(360)
                .Background(Theme.CardBackground)
                .Ref(canvasRef)
                .OnPointerPressed(OnPanelPressed)
                .WithKey($"panel-{generation}"),

                // ── Inspector ───────────────────────────────────────────────
                Border(
                    VStack(
                        Heading("Inspector"),
                        TextBlock(hit ?? "Click an element on the left.")
                            .Set(tb => tb.TextWrapping = TextWrapping.Wrap),
                        scan is null ? null : Border(
                            TextBlock(scan)
                                .FontFamily("Cascadia Mono, Consolas")
                                .FontSize(11)
                                .Set(tb => tb.TextWrapping = TextWrapping.NoWrap)
                        ).Padding(8).Background(Theme.SubtleFill)
                    ).Spacing(10)
                )
                .Padding(16)
                .Width(560)
                .Background(Theme.CardBackground)
            ).Spacing(16)
        ).Spacing(14).Padding(20);
    }
}
