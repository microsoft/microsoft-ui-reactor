using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.16 / §2.17 — DockManager renderer (Reactor-native, no XAML).
//
//  The native registration mounts a Border whose Child is reconciled from
//  the element this component returns. Translates DockManager.Layout into:
//    DockSplit       → FlexElement + DockSplitterElement (§2.1)
//    DockTabGroup    → TabViewElement (§2.2)
//    DockableContent → its Content element (leaf)
//
//  The component owns:
//    • a stable DockHostModel instance — `UseRef`-cached so identity is
//      preserved across renders; only mount/unmount invalidates it. The
//      model's Root / sides / ActiveContent are synced from the immutable
//      element snapshot each render (controlled-input pattern; live
//      mutation will follow at §2.4 drag pipeline).
//    • per-DockSplit ratio state (ConditionalWeakTable keyed by node ref).
//
//  Context publication (§2.17): the rendered subtree is wrapped with
//  Provide(Host=model), Provide(ActivePaneKey=active key),
//  Provide(LayoutSnapshot=snapshot). Each pane's Content is further
//  wrapped with Provide(Pane=DockPaneInfo) so UsePane() resolves.
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
        // from each node's stored Width/Height hints.
        var (ratioStore, setRatios) = UseState<ConditionalWeakTable<DockSplit, double[]>>(
            new ConditionalWeakTable<DockSplit, double[]>());

        // Stable DockHostModel instance for the lifetime of this component
        // (§2.16). UseRef keeps the same model object across renders so
        // UseDockHost() consumers don't churn on each layout-prop change.
        var modelRef = UseRef<DockHostModel?>(null);
        var model = modelRef.Current ??= new DockHostModel();
        SyncModelFromElement(model, manager);

        var activeKey = manager.ActiveDocument?.Key;
        var snapshot = BuildSnapshot(model);

        Element BuildNode(DockNode node) => node switch
        {
            DockSplit split => RenderSplit(split, ratioStore, setRatios, BuildNode),
            DockTabGroup grp => DockTabGroupRenderer.Render(
                grp,
                renderLeafContent: doc => WrapLeafWithPaneContext(doc),
                onSelectedIndexChanged: null,
                onTabClosing: null),
            DockableContent leaf => WrapLeafWithPaneContext(leaf),
            _ => new BorderElement(null),
        };

        Element body = manager.Layout is null
            ? new BorderElement(null)
            : BuildNode(manager.Layout);

        // ── Side strips — full popup expansion lands with §2.5; the
        // strips themselves are the anchor surface. Elide when empty so
        // the visual matches the P1 baseline for layouts that don't pin.
        var hasSides =
            (manager.LeftSide is { Count: > 0 }) ||
            (manager.TopSide is { Count: > 0 }) ||
            (manager.RightSide is { Count: > 0 }) ||
            (manager.BottomSide is { Count: > 0 });

        Element composed = hasSides
            ? DockSideStripRenderer.Compose(manager, body)
            : body;

        // §2.17 — publish the host model + active-key + layout-snapshot
        // context slots so descendant function components hooked into
        // DockContexts.Host / ActivePaneKey / LayoutSnapshot resolve to
        // the live state.
        return composed
            .Provide(DockContexts.Host, model)
            .Provide(DockContexts.ActivePaneKey, activeKey)
            .Provide(DockContexts.LayoutSnapshot, snapshot);
    }

    private static Element WrapLeafWithPaneContext(DockableContent leaf)
    {
        var content = leaf.Content ?? (Element)new BorderElement(null);
        var info = new DockPaneInfo(leaf.Key, leaf.Title ?? string.Empty, leaf);
        // PaneState for a docked leaf in the center tree is always Docked.
        // Floating / AutoHidden states are published by the floating window
        // host (§2.6) and the side-popup host (§2.5) respectively.
        return content
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.Docked);
    }

    private static void SyncModelFromElement(DockHostModel model, DockManager element)
    {
        model.Root = element.Layout;
        model.LeftSide = SideSlice(element.LeftSide);
        model.TopSide = SideSlice(element.TopSide);
        model.RightSide = SideSlice(element.RightSide);
        model.BottomSide = SideSlice(element.BottomSide);
        model.ActiveContent = element.ActiveDocument;
        // Floating window state survives the §2.6 wire-up; today it stays
        // empty until the floating renderer publishes entries.
    }

    private static IReadOnlyList<ToolWindow> SideSlice(IReadOnlyList<DockableContent>? items)
    {
        if (items is null or { Count: 0 }) return Array.Empty<ToolWindow>();
        var buffer = new List<ToolWindow>(items.Count);
        foreach (var item in items)
        {
            if (item is ToolWindow tw) buffer.Add(tw);
            // Bare DockableContent in a side slot is a P1 carry-over shape;
            // §2.8 deprecates the bare base type. Drop silently — the model
            // exposes only ToolWindow per the spec's typed surface.
        }
        return buffer;
    }

    private static DockLayoutSnapshot BuildSnapshot(DockHostModel model) =>
        new(
            Root: model.Root,
            LeftSide: model.LeftSide,
            TopSide: model.TopSide,
            RightSide: model.RightSide,
            BottomSide: model.BottomSide,
            Floating: model.Floating,
            ActiveContent: model.ActiveContent);

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
            onSplitterDelta: (idx, delta, hostExtent, isFinal) =>
            {
                if (delta == 0) return;
                // hostExtent < 1 means the FlexPanel hasn't been laid out
                // yet (control just attached, arrangement pending). Skip
                // the mutation rather than divide by zero / a tiny number.
                if (hostExtent < 1) return;

                var perChild = new DockSplitChild[children.Count];
                for (int i = 0; i < children.Count; i++)
                    perChild[i] = new DockSplitChild(ratios[i], MinDip: 60, MaxDip: double.PositiveInfinity);

                var sol = DockSplitSolver.ApplyDelta(perChild, idx, delta, totalDip: hostExtent);
                var newRatios = sol.Ratios;
                ratioStore.AddOrUpdate(split, newRatios);
                // Mutate the live array so subsequent pointer-move events in
                // the same drag see the updated values without waiting for
                // the next render pass.
                for (int i = 0; i < ratios.Length; i++) ratios[i] = newRatios[i];
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
