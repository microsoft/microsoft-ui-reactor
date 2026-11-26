using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reference edges (LabeledBy, DescribedBy, ...) for controls and modifiers.
internal static class ReferenceEdgeWiring
{
    internal static ReferenceEdge GetOrCreateReferenceEdge(FrameworkElement ctrl, int slot)
    {
        var state = Reconciler.GetOrCreateReactorState(ctrl);
        state.ReferenceEdges ??= new ReferenceEdgeBag();
        if (!state.ReferenceEdges.Edges.TryGetValue(slot, out var edge))
        {
            edge = new ReferenceEdge();
            state.ReferenceEdges.Edges[slot] = edge;
        }

        return edge;
    }

    internal static ReferenceListEdge GetOrCreateReferenceListEdge(FrameworkElement ctrl, int slot)
    {
        var state = Reconciler.GetOrCreateReactorState(ctrl);
        state.ReferenceEdges ??= new ReferenceEdgeBag();
        if (!state.ReferenceEdges.ListEdges.TryGetValue(slot, out var edge))
        {
            edge = new ReferenceListEdge();
            state.ReferenceEdges.ListEdges[slot] = edge;
        }

        return edge;
    }

    internal static void WireReferenceEdge(
        FrameworkElement ctrl,
        int slot,
        Microsoft.UI.Reactor.Input.ElementRef? cell,
        Action<FrameworkElement, FrameworkElement?> apply)
    {
        var edge = GetOrCreateReferenceEdge(ctrl, slot);
        edge.Apply = apply; // retained so teardown can clear the target property (CR-002)
        if (cell is null)
        {
            if (edge.Cell is not null)
            {
                edge.Cell.CurrentChanged -= edge.Handler;
                edge.Cell = null;
                edge.Handler = null;
            }
            apply(ctrl, null);
            return;
        }

        if (ReferenceEquals(edge.Cell, cell)) return;

        if (edge.Cell is not null)
            edge.Cell.CurrentChanged -= edge.Handler;

        edge.Cell = cell;
        edge.Handler = target => apply(ctrl, target);
        cell.CurrentChanged += edge.Handler;
        apply(ctrl, cell.Current);
    }

    internal static void WireReferenceListEdge(
        FrameworkElement ctrl,
        int slot,
        IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef>? cells,
        Action<FrameworkElement> recompute,
        Action<FrameworkElement>? clearTarget = null)
    {
        var edge = GetOrCreateReferenceListEdge(ctrl, slot);
        edge.Recompute = recompute;
        edge.Clear = clearTarget; // retained so teardown can empty the target list (CR-002)
        edge.Authored = cells;
        edge.Handler ??= _ => edge.Recompute?.Invoke(ctrl);

        var next = new List<Microsoft.UI.Reactor.Input.ElementRef>();
        if (cells is not null)
        {
            foreach (var cell in cells)
            {
                if (cell is null) continue;
                if (!next.Any(existing => ReferenceEquals(existing, cell)))
                    next.Add(cell);
            }
        }

        for (int i = edge.Cells.Count - 1; i >= 0; i--)
        {
            var existing = edge.Cells[i];
            if (next.Any(cell => ReferenceEquals(cell, existing))) continue;
            existing.CurrentChanged -= edge.Handler;
            edge.Cells.RemoveAt(i);
        }

        foreach (var cell in next)
        {
            if (edge.Cells.Any(existing => ReferenceEquals(existing, cell))) continue;
            edge.Cells.Add(cell);
            cell.CurrentChanged += edge.Handler;
        }

        recompute(ctrl);
    }

    internal static void TeardownReferenceEdges(FrameworkElement ctrl)
    {
        if (ctrl.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState state)
            return;
        // A deferred .LabeledBy("id") that never resolved is reference state too: a retained,
        // unpooled control must not keep reporting it after unmount.
        state.PendingLabeledBy = null;
        if (state.ReferenceEdges is null)
            return;

        foreach (var edge in state.ReferenceEdges.Edges.Values)
        {
            if (edge.Cell is not null)
                edge.Cell.CurrentChanged -= edge.Handler;
            // Clear the target property so a held/pooled control doesn't keep a stale
            // relationship (e.g. XYFocusRight / LabeledBy / TeachingTip.Target) — CR-002.
            edge.Apply?.Invoke(ctrl, null);
            edge.Cell = null;
            edge.Handler = null;
            edge.Apply = null;
        }

        foreach (var edge in state.ReferenceEdges.ListEdges.Values)
        {
            foreach (var cell in edge.Cells)
                cell.CurrentChanged -= edge.Handler;
            // Empty the target relationship list (DescribedBy / FlowsTo / FlowsFrom) — CR-002.
            edge.Clear?.Invoke(ctrl);
            edge.Cells.Clear();
            edge.Authored = null;
            edge.Handler = null;
            edge.Recompute = null;
            edge.Clear = null;
        }

        state.ReferenceEdges.Edges.Clear();
        state.ReferenceEdges.ListEdges.Clear();
        state.ReferenceEdges = null;
    }

    internal static void ApplyModifierReferenceEdges(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        WireModifierScalarReference(
            fe,
            ReferenceSlots.ModifierRef_LabeledBy,
            m.Accessibility?.LabeledByRef,
            oldM?.Accessibility?.LabeledByRef,
            static (c, target) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(c, target));

        WireModifierReferenceList(
            fe,
            ReferenceSlots.ModifierRef_DescribedBy,
            m.Accessibility?.DescribedByRefs,
            oldM?.Accessibility?.DescribedByRefs,
            static c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetDescribedBy(c));

        WireModifierReferenceList(
            fe,
            ReferenceSlots.ModifierRef_FlowsTo,
            m.Accessibility?.FlowsToRefs,
            oldM?.Accessibility?.FlowsToRefs,
            static c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetFlowsTo(c));

        WireModifierReferenceList(
            fe,
            ReferenceSlots.ModifierRef_FlowsFrom,
            m.Accessibility?.FlowsFromRefs,
            oldM?.Accessibility?.FlowsFromRefs,
            static c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetFlowsFrom(c));

        WireModifierScalarReference(
            fe,
            ReferenceSlots.ModifierRef_XYFocusUp,
            m.XYFocusUpRef,
            oldM?.XYFocusUpRef,
            static (c, target) => c.XYFocusUp = target);

        WireModifierScalarReference(
            fe,
            ReferenceSlots.ModifierRef_XYFocusDown,
            m.XYFocusDownRef,
            oldM?.XYFocusDownRef,
            static (c, target) => c.XYFocusDown = target);

        WireModifierScalarReference(
            fe,
            ReferenceSlots.ModifierRef_XYFocusLeft,
            m.XYFocusLeftRef,
            oldM?.XYFocusLeftRef,
            static (c, target) => c.XYFocusLeft = target);

        WireModifierScalarReference(
            fe,
            ReferenceSlots.ModifierRef_XYFocusRight,
            m.XYFocusRightRef,
            oldM?.XYFocusRightRef,
            static (c, target) => c.XYFocusRight = target);

        // ToolTipService.PlacementTarget takes a UIElement, so an unset target
        // has to go through ClearValue rather than a null assignment.
        WireModifierScalarReference(
            fe,
            ReferenceSlots.ModifierRef_ToolTipPlacementTarget,
            m.ToolTipPlacementTargetRef,
            oldM?.ToolTipPlacementTargetRef,
            static (c, target) =>
            {
                if (target is not null) WinUI.ToolTipService.SetPlacementTarget(c, target);
                else c.ClearValue(WinUI.ToolTipService.PlacementTargetProperty);
            });
    }

    private static void WireModifierScalarReference(
        FrameworkElement fe,
        int slot,
        Microsoft.UI.Reactor.Input.ElementRef? current,
        Microsoft.UI.Reactor.Input.ElementRef? old,
        Action<FrameworkElement, FrameworkElement?> apply)
    {
        if (current is not null || old is not null)
            WireReferenceEdge(fe, slot, current, apply);
    }

    private static void WireModifierReferenceList(
        FrameworkElement fe,
        int slot,
        IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef>? current,
        IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef>? old,
        Func<FrameworkElement, IList<DependencyObject>> getDestination)
    {
        if (current is null && old is null) return;

        List<Microsoft.UI.Reactor.Input.ElementRef>? cells = null;
        if (current is not null)
        {
            cells = new(current.Count);
            foreach (var r in current)
                if (r is not null)
                    cells.Add(r);
        }

        WireReferenceListEdge(
            fe,
            slot,
            cells,
            ctrl =>
            {
                var dst = getDestination(ctrl);
                dst.Clear();
                if (cells is null) return;

                foreach (var cell in cells)
                {
                    if (cell.Current is FrameworkElement target)
                        dst.Add(target);
                }
            },
            clearTarget: ctrl => getDestination(ctrl).Clear());
    }

    [global::System.Diagnostics.Conditional("DEBUG")]
    internal static void AssertTypedRefMatch(Microsoft.UI.Reactor.Input.ElementRef r, FrameworkElement fe)
    {
        // Spec 033 §3 — typed refs (ElementRef<T>) record their expected concrete
        // type on the inner untyped ref. When a typed ref is bound to an element
        // that is not assignable to T, fail loudly under DEBUG so the bug is caught
        // during dev-loop. RELEASE builds keep the silent-null behavior of the
        // typed wrapper's `Current` property — no perf cost on the hot path.
        var expected = r.ExpectedType;
        if (expected is null) return;
        if (!expected.IsInstanceOfType(fe))
        {
            global::System.Diagnostics.Debug.Fail(
                $"ElementRef<{expected.Name}> attached to a {fe.GetType().Name}. " +
                "Use ElementRef<" + fe.GetType().Name + "> or untyped ElementRef.");
        }
    }
}
