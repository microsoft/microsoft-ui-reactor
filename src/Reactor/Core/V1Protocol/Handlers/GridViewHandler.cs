using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 3 prelude — GridView port (closes the deferred dispatch
/// carve so <see cref="GridViewElement"/> routes through V1).
///
/// <para><b>Path B (delegate, no children strategy):</b> mirrors the Phase 1
/// <see cref="ListViewHandler"/> exactly. Delegates Mount / Update to the
/// engine's internal <see cref="Reconciler.MountGridView"/> /
/// <see cref="Reconciler.UpdateGridView"/> bodies, which install the same
/// <c>ItemsSource = Range(0..N) + shared ItemTemplate + ContainerContentChanging</c>
/// lazy container-realization contract as ListView (the
/// <c>GridViewDescriptor</c>'s <c>ItemsHost&lt;&gt;</c> strategy is
/// intentionally <i>not</i> registered — it pre-mounts every item with no
/// virtualization, diverging from this legacy behavior).</para>
///
/// <para><c>Children = null</c> because the delegate body fully owns child
/// realization. Unmount uses the default <c>CollectSelf</c> disposition
/// (matching ListView): realized containers are torn down by the recycle arm
/// of <c>ContainerContentChanging</c>, so behavior is identical V1 ON ≡
/// V1 OFF.</para>
/// </summary>
internal sealed class GridViewHandler : IElementHandler<GridViewElement, WinUI.GridView>
{
    public WinUI.GridView Mount(MountContext ctx, GridViewElement el)
        => ctx.Reconciler.MountGridView(el, ctx.RequestRerender);

    public void Update(UpdateContext ctx, GridViewElement oldEl, GridViewElement newEl, WinUI.GridView ctrl)
        => ctx.Reconciler.UpdateGridView(oldEl, newEl, ctrl, ctx.RequestRerender);

    public ChildrenStrategy<GridViewElement, WinUI.GridView>? Children => null;
}
