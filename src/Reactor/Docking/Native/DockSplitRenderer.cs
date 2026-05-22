using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.1 — translate a DockSplit node into a Reactor element tree.
//
//  Walks a DockSplit + a per-child Element subtree, weaves in splitters
//  between adjacent children, and returns a FlexElement that the reconciler
//  renders to a FlexPanel (Yoga-backed; precedent: FlexPanelDemo). Ratios
//  drive flex.grow on each child; per-child min/max are reserved for the
//  full constraint solver once the live model exposes them (no clamping is
//  done at render time — clamping is the solver's job and happens on
//  splitter delta).
// ════════════════════════════════════════════════════════════════════════

internal static class DockSplitRenderer
{
    /// <summary>
    /// Compose a flex container for a <see cref="DockSplit"/>.
    /// </summary>
    /// <param name="split">The dock split node.</param>
    /// <param name="ratios">
    /// Current ratios (one per child). Length must equal
    /// <c>split.Children.Count</c>. Caller is responsible for keeping the
    /// ratio array in sync with the model.
    /// </param>
    /// <param name="renderChild">
    /// Renders the Element subtree for one <see cref="DockNode"/> child.
    /// </param>
    /// <param name="onSplitterDelta">
    /// Invoked when a splitter handle reports a pointer/keyboard delta.
    /// Signature: <c>(splitterIndex, deltaDip, hostExtentDip, isFinal) =&gt; ...</c>.
    /// <c>hostExtentDip</c> is the measured size of the parent FlexPanel along
    /// the split axis at the moment of the event, so the consumer can convert
    /// pixel deltas into ratio space without assuming a synthetic total.
    /// </param>
    /// <param name="splitterDiagnosticSink">
    /// Optional callback that receives PRESS / MOVE / RELEASE / SOLVE
    /// trace strings — used by the spec 045 operation log to capture the
    /// math behind each splitter drag.
    /// </param>
    /// <remarks>
    /// RTL is resolved upstream by Yoga via <c>FlexPanel.LayoutDirection</c>
    /// (see <c>FlexDirectionHelper.ResolveDirection</c>), so this renderer
    /// emits children in document order and lets the panel flip row layouts
    /// at measure time.
    /// </remarks>
    public static Element Render(
        Microsoft.UI.Reactor.Docking.DockSplit split,
        IReadOnlyList<double> ratios,
        Func<Microsoft.UI.Reactor.Docking.DockNode, Element> renderChild,
        Action<int, double, double, bool> onSplitterDelta,
        Action<string>? splitterDiagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(ratios);
        ArgumentNullException.ThrowIfNull(renderChild);
        ArgumentNullException.ThrowIfNull(onSplitterDelta);

        var children = split.Children;
        if (children.Count != ratios.Count)
            throw new ArgumentException(
                $"Ratio count ({ratios.Count}) must match child count ({children.Count}).",
                nameof(ratios));

        var isRow = split.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
        var direction = isRow ? FlexDirection.Row : FlexDirection.Column;
        var splitterDir = isRow ? DockSplitterDirection.Columns : DockSplitterDirection.Rows;

        var n = children.Count;
        // Setters intentionally empty for now — re-issuing them each render
        // (even when the value doesn't change) was suspected of causing
        // child detach/reattach. RTL flip will re-attach via a stable
        // reference once we confirm the splitter stays mounted.
        Action<FlexPanel>[] setters = [];
        if (n == 0)
            return new FlexElement([]) { Direction = direction, Setters = setters };

        // Children interleaved with splitters: [c0, s0, c1, s1, ..., c(n-1)].
        var composed = new List<Element>(2 * n - 1);
        for (int i = 0; i < n; i++)
        {
            var rawRatio = ratios[i];
            var grow = rawRatio < 0 || double.IsNaN(rawRatio) ? 0 : rawRatio;
            var child = renderChild(children[i]);
            // basis: 0 is the CSS-flexbox-equivalent of WinUI Grid's
            // `GridUnitType.Star`. Without it, each pane's basis defaults
            // to its intrinsic content size, and grow only distributes
            // FREE space beyond basis — so a pane with heavy content
            // (TabView with many tabs + body) hoards width and grow
            // changes do nothing visible. With basis=0, grow drives the
            // ENTIRE pane size proportionally — splitter drags move
            // panes 1:1 with the cursor and the ratios actually mean
            // what they say.
            composed.Add(child.Flex(grow: grow, shrink: 1, basis: 0));

            if (i < n - 1)
            {
                var splitterIndex = i;
                composed.Add((new DockSplitterElement(
                    Direction: splitterDir,
                    OnDelta: (delta, hostExtent, isFinal) =>
                        onSplitterDelta(splitterIndex, delta, hostExtent, isFinal))
                    {
                        DiagnosticSink = splitterDiagnosticSink,
                    })
                    .Flex(grow: 0, shrink: 0));
            }
        }

        return new FlexElement(composed.ToArray())
        {
            Direction = direction,
            AlignItems = FlexAlign.Stretch,
            Wrap = FlexWrap.NoWrap,
            Setters = setters,
        };
    }
}
