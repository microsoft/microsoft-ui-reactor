using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 1 (1.15) — templated items host port. The most
/// complex of the five Phase 1 ports: WinUI <see cref="WinUI.ListView"/>
/// drives container realization through <c>ContainerContentChanging</c>
/// + a shared <c>DataTemplate</c> + <c>ItemsSource</c>.
///
/// <para><b>Path B (delegate, no children strategy):</b> this handler
/// delegates to the existing internal <see cref="Reconciler.MountListView"/> /
/// <see cref="Reconciler.UpdateListView"/> helpers, which install their
/// own container-realization hook and run the spec-042 keyed-reconcile
/// path internally. The handler returns <c>Children = null</c> because
/// the delegate body fully owns children dispatch — no strategy dispatch
/// is needed (or would be correct) on top.</para>
///
/// <para>§14 Phase 3-final note: the new <see cref="ItemsHost{TElement,TControl}"/>
/// shape (with <c>GetItems</c> / <c>GetCollection</c>) is for descriptor
/// authors of items-collection controls (<c>ListBox</c>,
/// <c>ComboBox.Items</c>, <c>RadioButtons.Items</c>). Typed templated lists
/// (<c>ListView&lt;T&gt;</c> etc.) get their own ItemsHost-aware descriptor
/// port in Batch G2 with spec-042 keyed reconciliation threaded
/// through.</para>
/// </summary>
internal sealed class ListViewHandler : IElementHandler<ListViewElement, WinUI.ListView>
{
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

    public ChildrenStrategy<ListViewElement, WinUI.ListView>? Children => null;
}
