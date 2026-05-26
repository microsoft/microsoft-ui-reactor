using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 1 (1.15) — templated items host port. The most
/// complex of the five Phase 1 ports: WinUI <see cref="WinUI.ListView"/>
/// drives container realization through <c>ContainerContentChanging</c>
/// + a shared <c>DataTemplate</c> + <c>ItemsSource</c>, and the v1
/// <see cref="ItemsHost{TElement,TControl}"/> strategy dispatch is
/// intentionally a no-op (the real path goes through spec-042
/// <c>ChildReconciler</c> via the existing realization hook).
///
/// <para><b>Path B (delegate + shape):</b> per the 1.15 task plan, this
/// handler delegates to the existing internal
/// <see cref="Reconciler.MountListView"/> / <see cref="Reconciler.UpdateListView"/>
/// helpers. The V1 ON and V1 OFF paths execute identical bodies, so the
/// keyed-reconcile integration that spec-042 ChildReconciler already
/// handles flows through the v1 path transparently. The
/// <see cref="ItemsHost{TElement,TControl}"/> strategy is declared for
/// shape compliance — V1HandlerAdapter intentionally no-ops on it.</para>
///
/// <para>Phase 3 turns this into a full re-implementation when keyed-list
/// reconciliation moves into the strategy directly (per spec §6).</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class ListViewHandler : IElementHandler<ListViewElement, WinUI.ListView>
{
    // Static instance so each Mount doesn't reallocate the strategy record.
    private static readonly ItemsHost<ListViewElement, WinUI.ListView> ChildrenStrategy =
        new ItemsHost<ListViewElement, WinUI.ListView>(
            GetItemsSource: el => (IEnumerable)el.Items,
            // The fallback for IItemsContainer (per Batch 2 report) is the
            // control itself — the actual container resolution lives in the
            // realization hook installed by MountListView.
            GetContainer: ctrl => ctrl,
            Options: new ItemsHostOptions());

    public WinUI.ListView Mount(MountContext ctx, ListViewElement el)
    {
        // Delegate to the engine's shared MountListView body. V1HandlerAdapter
        // will re-tag the control after we return (idempotent — the legacy
        // body already tagged it, which is harmless).
        return ctx.Reconciler.MountListView(el, ctx.RequestRerender);
    }

    public void Update(UpdateContext ctx, ListViewElement oldEl, ListViewElement newEl, WinUI.ListView ctrl)
    {
        ctx.Reconciler.UpdateListView(oldEl, newEl, ctrl, ctx.RequestRerender);
    }

    public ChildrenStrategy<ListViewElement, WinUI.ListView>? Children => ChildrenStrategy;
}
