using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 3 prelude — TabView port (closes the deferred dispatch
/// carve so <see cref="TabViewElement"/> routes through V1).
///
/// <para><b>Path B (delegate, no children strategy):</b> delegates Mount /
/// Update to the engine's existing internal
/// <see cref="Reconciler.MountTabView"/> /
/// <see cref="Reconciler.UpdateTabView"/> bodies — the COMPLETE legacy
/// implementation that already owns the spec 045 §2.4 docking drag pipeline,
/// §2.2 pinnable tab headers, in-place tab-content reconcile, conditional
/// SelectedIndex write, and the TabStripHeader / TabStripFooter Element
/// slots. This is distinct from the (unregistered) <c>TabViewDescriptor</c> +
/// <c>TabItemsHost</c> port, which intentionally leaves those features on the
/// legacy arm; the delegate runs the full feature set because it IS the
/// legacy code.</para>
///
/// <para><b>No control substitution:</b> <see cref="Reconciler.UpdateTabView"/>
/// always returns <c>null</c> (pure in-place reconcile), so the void
/// <see cref="IElementHandler{TElement,TControl}.Update"/> shape preserves
/// behavior — identity is never swapped on update.</para>
///
/// <para><b>Unmount parity:</b> the standard adapter returns
/// <see cref="V1UnmountDisposition.CollectSelf"/>, which pools the TabView
/// and stops traversal in both unmount paths. V1 OFF reaches the same
/// outcome — <c>WinUI.TabView</c> is an <c>ItemsControl</c> not matched by
/// any of the Panel / Border / ScrollViewer / UserControl / ContentControl
/// recursion branches, so the legacy fall-through also pools the control
/// without recursing into tab content. Mount/update under V1 ON are likewise
/// unchanged from the previously-carved V1-ON path (which already ran
/// <c>MountTabView</c>/<c>UpdateTabView</c> via the legacy switch);
/// registering this handler only makes the parity-safe unmount arm fire.
/// Cleanup is therefore byte-identical V1 ON ≡ V1 OFF, so this handler
/// intentionally does not override <c>Unmount</c>.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class TabViewHandler : IElementHandler<TabViewElement, WinUI.TabView>
{
    public WinUI.TabView Mount(MountContext ctx, TabViewElement el)
        => ctx.Reconciler.MountTabView(el, ctx.RequestRerender);

    public void Update(UpdateContext ctx, TabViewElement oldEl, TabViewElement newEl, WinUI.TabView ctrl)
        => ctx.Reconciler.UpdateTabView(oldEl, newEl, ctrl, ctx.RequestRerender);

    public ChildrenStrategy<TabViewElement, WinUI.TabView>? Children => null;
}
