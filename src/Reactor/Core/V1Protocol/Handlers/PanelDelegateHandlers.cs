using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 3 prelude — panel keyed-reconcile closure.
//
// The V1 Panel<> children strategy (V1HandlerAdapter.cs:231-290) reconciles
// children BY INDEX with no keyed reconcile (a deliberately-deferred Phase 3
// follow-up — see the "Phase 1 limitation" comment there). That loses WinUI
// control identity on keyed reorder/reverse/swap/remove-middle, diverging from
// the legacy hand-coded panel update methods which call ReconcileChildren ->
// ChildReconciler (the spec-042 keyed LIS + positional diff) and re-apply
// attached props (Grid.Row/Column, Flex, Canvas, WrapGrid spans, RelativePanel
// sibling refs). Under V1 ON the descriptor path failed ~17 identity-preservation
// fixtures (FlexColumn_KeyedChildren_*, KeyedReorder_Reused, LargeKeyed_*,
// MultiCycle_*, Reconciler_KeyedList_ControlReused) that pass under V1 OFF.
//
// Fix: route each panel through a Path B delegate decorator that runs the
// COMPLETE legacy Mount*/Update* body (identical to V1 OFF). Decorator shape
// (ContinueDefaultTraversal) so unmount falls through to the type-based
// recursion V1 OFF uses — every control here is a WinUI.Panel subclass and the
// first branch of BOTH unmount paths (UnmountRecursive / UnmountAndCollect)
// recurses panel.Children then pools the panel, so teardown is byte-identical.
// Update returns the legacy result when non-null (UpdateRelativePanel may
// substitute the whole control on a child-count change) and otherwise keeps
// the existing control. The matching descriptor types are retained for their
// isolated selftests + perf-bench variant (same pattern as ButtonDescriptor).

/// <summary>§14 prelude — Flex (CSS flexbox panel; keyed reconcile + flex attached).</summary>
internal sealed class FlexPanelHandler : IDecoratorElementHandler<FlexElement>
{
    public UIElement Mount(MountContext ctx, FlexElement el)
        => ctx.Reconciler.MountFlex(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, FlexElement oldEl, FlexElement newEl, UIElement control)
        => ctx.Reconciler.UpdateFlex(oldEl, newEl, (Layout.FlexPanel)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, FlexElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — StackPanel (VStack/HStack; keyed reconcile).</summary>
internal sealed class StackPanelHandler : IDecoratorElementHandler<StackElement>
{
    public UIElement Mount(MountContext ctx, StackElement el)
        => ctx.Reconciler.MountStack(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, StackElement oldEl, StackElement newEl, UIElement control)
        => ctx.Reconciler.UpdateStack(oldEl, newEl, (WinUI.StackPanel)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, StackElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — Grid (keyed reconcile on count change + grid placement).</summary>
internal sealed class GridPanelHandler : IDecoratorElementHandler<GridElement>
{
    public UIElement Mount(MountContext ctx, GridElement el)
        => ctx.Reconciler.MountGrid(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, GridElement oldEl, GridElement newEl, UIElement control)
        => ctx.Reconciler.UpdateGrid(oldEl, newEl, (WinUI.Grid)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, GridElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — Canvas (keyed reconcile + Canvas.Left/Top).</summary>
internal sealed class CanvasPanelHandler : IDecoratorElementHandler<CanvasElement>
{
    public UIElement Mount(MountContext ctx, CanvasElement el)
        => ctx.Reconciler.MountCanvas(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, CanvasElement oldEl, CanvasElement newEl, UIElement control)
        => ctx.Reconciler.UpdateCanvas(oldEl, newEl, (WinUI.Canvas)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CanvasElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — RelativePanel (sibling-ref attached; Update may substitute the control).</summary>
internal sealed class RelativePanelHandler : IDecoratorElementHandler<RelativePanelElement>
{
    public UIElement Mount(MountContext ctx, RelativePanelElement el)
        => ctx.Reconciler.MountRelativePanel(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, RelativePanelElement oldEl, RelativePanelElement newEl, UIElement control)
        => ctx.Reconciler.UpdateRelativePanel(oldEl, newEl, (WinUI.RelativePanel)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, RelativePanelElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — VariableSizedWrapGrid (keyed reconcile + spans).</summary>
internal sealed class WrapGridHandler : IDecoratorElementHandler<WrapGridElement>
{
    public UIElement Mount(MountContext ctx, WrapGridElement el)
        => ctx.Reconciler.MountWrapGrid(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, WrapGridElement oldEl, WrapGridElement newEl, UIElement control)
        => ctx.Reconciler.UpdateWrapGrid(oldEl, newEl, (WinUI.VariableSizedWrapGrid)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, WrapGridElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
