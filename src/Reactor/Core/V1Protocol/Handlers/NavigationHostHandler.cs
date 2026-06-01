using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 3 prelude / Phase 4 §4.0.2 — NavigationHost port (closes
/// the deferred dispatch carve so <see cref="NavigationHostElement"/> routes
/// through V1).
///
/// <para><b>Path B (delegate, no children strategy):</b> delegates Mount /
/// Update to the V1-owned <see cref="NavigationHostLifecycle"/>, which owns the
/// per-instance route / cache / transition state tracked in the reconciler's
/// <c>_navigationHostNodes</c> table. <c>Children = null</c> because the
/// lifecycle fully owns child mount/swap inside the host Grid.</para>
///
/// <para><b>Unmount (§4.0.2):</b> teardown is now owned by this handler —
/// <see cref="Unmount"/> calls <see cref="Reconciler.CleanupNavigationHostNode"/>
/// (detach RouteChanged, clear cache, unmount the current child) and the adapter
/// returns <c>CollectSelf</c>, so the engine does not recurse into the Grid
/// children again. The flag-independent pre-dispatch intercept that previously
/// did this in <c>UnmountRecursive</c> is now a V1-OFF-only fallback (deleted
/// with the V1-OFF escape path in §4.6), so cleanup is byte-identical V1 ON ≡
/// V1 OFF. The defensive "lost tracking" remount inside
/// <see cref="NavigationHostLifecycle.Update"/> is unreachable under normal V1
/// operation (the node is always present after <c>Mount</c>), so the void
/// <see cref="Update"/> here — which cannot substitute the control — preserves
/// behavior.</para>
/// </summary>
internal sealed class NavigationHostHandler : IElementHandler<NavigationHostElement, WinUI.Grid>
{
    public WinUI.Grid Mount(MountContext ctx, NavigationHostElement el)
        => NavigationHostLifecycle.Mount(ctx.Reconciler, el, ctx.RequestRerender);

    public void Update(UpdateContext ctx, NavigationHostElement oldEl, NavigationHostElement newEl, WinUI.Grid ctrl)
        => NavigationHostLifecycle.Update(ctx.Reconciler, oldEl, newEl, ctrl, ctx.RequestRerender);

    public void Unmount(UnmountContext ctx, WinUI.Grid control)
        => ctx.Reconciler.CleanupNavigationHostNode(control);

    public ChildrenStrategy<NavigationHostElement, WinUI.Grid>? Children => null;
}
