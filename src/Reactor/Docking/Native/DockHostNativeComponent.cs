using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.16 — DockManager renderer (Reactor-native, no XAML control).
//
//  The native registration mounts a Border whose Child is reconciled from
//  the element this component returns. Translates DockManager.Layout into
//  a tree of:
//    DockSplit       → FlexElement + DockSplitterElement (§2.1)
//    DockTabGroup    → TabViewElement (§2.2)
//    DockableContent → its Content element (leaf)
//
//  Phase-2 progressive enhancement (intentionally not yet wired):
//    • Side strips (LeftSide / TopSide / RightSide / BottomSide) —
//      lands with §2.5 side popup.
//    • Drop-target overlay — lands with §2.3.
//    • Drag/drop pipeline — lands with §2.4.
//    • Floating window mounts — lands with §2.6.
//    • Live DockHostModel mutations driving the tree — lands with the
//      §2.16 "reconciler reads from model" item once the model is the
//      source of truth (today, the immutable element snapshot is).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Props for <see cref="DockHostNativeComponent"/> — the immutable input
/// from the parent render pass. Equality on the inner element drives
/// <see cref="Component{TProps}.ShouldUpdate"/>.
/// </summary>
internal sealed record DockHostNativeProps(DockManager Manager);

internal sealed class DockHostNativeComponent : Component<DockHostNativeProps>
{
    public override Element Render()
    {
        var manager = Props.Manager;

        // Per-DockSplit ratio state. Keyed by reference identity of the
        // DockSplit node — when the app rebuilds Layout each render, new
        // DockSplit instances are created and the dictionary is rebuilt
        // from each node's stored Width/Height hints. Pointer-driven
        // ratios survive only as long as the DockSplit reference is
        // stable (typical case: app holds a `useState` Layout and reuses
        // it across renders). Survival across rebuilds is the §2.16
        // model-tracker job, not the renderer's.
        var (ratioStore, setRatios) = UseState<ConditionalWeakTable<DockSplit, double[]>>(
            new ConditionalWeakTable<DockSplit, double[]>());

        Element BuildNode(DockNode node) => node switch
        {
            DockSplit split => RenderSplit(split, ratioStore, setRatios, BuildNode),
            DockTabGroup grp => DockTabGroupRenderer.Render(
                grp,
                renderLeafContent: doc => doc.Content,
                onSelectedIndexChanged: null,
                onTabClosing: null),
            DockableContent leaf => leaf.Content ?? new BorderElement(null),
            _ => new BorderElement(null),
        };

        Element body = manager.Layout is null
            ? new BorderElement(null)
            : BuildNode(manager.Layout);

        // ── Side strips (left/top/right/bottom) — minimal P2 cut: a thin
        // strip on each side listing pinned tool windows by title. Full
        // side-popup expansion lands with §2.5; the strip itself is the
        // anchor the popup attaches to. For now we elide the strips
        // entirely when empty so the showcase keeps the P1 visual shape.
        var hasSides =
            (manager.LeftSide is { Count: > 0 }) ||
            (manager.TopSide is { Count: > 0 }) ||
            (manager.RightSide is { Count: > 0 }) ||
            (manager.BottomSide is { Count: > 0 });

        if (!hasSides) return body;

        // Compose center + sides into a 3-row × 3-col grid:
        //   [   ][ top   ][   ]
        //   [lft][center ][rgt]
        //   [   ][bottom ][   ]
        return DockSideStripRenderer.Compose(manager, body);
    }

    private static Element RenderSplit(
        DockSplit split,
        ConditionalWeakTable<DockSplit, double[]> ratioStore,
        Action<ConditionalWeakTable<DockSplit, double[]>> setRatios,
        Func<DockNode, Element> renderChild)
    {
        var children = split.Children;
        if (!ratioStore.TryGetValue(split, out var ratios) || ratios is null || ratios.Length != children.Count)
        {
            ratios = BootstrapRatios(split);
            ratioStore.AddOrUpdate(split, ratios);
        }

        return DockSplitRenderer.Render(
            split,
            ratios,
            renderChild,
            onSplitterDelta: (idx, delta, isFinal) =>
            {
                if (delta == 0) return;
                var perChild = new DockSplitChild[children.Count];
                for (int i = 0; i < children.Count; i++)
                    perChild[i] = new DockSplitChild(ratios[i], MinDip: 60, MaxDip: double.PositiveInfinity);

                // Total DIPs along the axis is not known at the model
                // layer — we use a synthetic 1000 unit so the delta is
                // interpreted in the same DIP space the FlexPanel arranged.
                // Once the renderer tracks the FlexPanel's ActualWidth /
                // ActualHeight via a ref the splitter delegates produce
                // pixel-accurate clamping; for the first cut, ratio drift
                // is acceptable.
                var sol = DockSplitSolver.ApplyDelta(perChild, idx, delta, totalDip: 1000);
                var newRatios = sol.Ratios;
                ratioStore.AddOrUpdate(split, newRatios);
                // Mutate the live array so the next splitter event sees
                // the updated values without waiting for the next render.
                for (int i = 0; i < ratios.Length; i++) ratios[i] = newRatios[i];
                // Trigger re-render by setting state with the same store
                // reference (state setter compares reference; we need a
                // new ConditionalWeakTable wrapper to force).
                setRatios(ratioStore);
            });
    }

    private static double[] BootstrapRatios(DockSplit split)
    {
        var n = split.Children.Count;
        if (n == 0) return [];

        var raw = new double[n];
        bool anyExplicit = false;
        for (int i = 0; i < n; i++)
        {
            double? hint = split.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
                ? (split.Children[i] as DockSplit)?.Width
                    ?? (split.Children[i] as DockTabGroup)?.Width
                    ?? (split.Children[i] as DockableContent)?.Width
                : (split.Children[i] as DockSplit)?.Height
                    ?? (split.Children[i] as DockTabGroup)?.Height
                    ?? (split.Children[i] as DockableContent)?.Height;
            if (hint is double v and > 0)
            {
                raw[i] = v;
                anyExplicit = true;
            }
        }
        return anyExplicit ? DockSplitSolver.Normalize(raw) : DockSplitSolver.EqualShare(n);
    }
}
