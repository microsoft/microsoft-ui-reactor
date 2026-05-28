using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 3 prelude — NavigationHost port (closes the deferred
/// dispatch carve so <see cref="NavigationHostElement"/> routes through V1).
///
/// <para><b>Path B (delegate, no children strategy):</b> delegates Mount /
/// Update to the engine's existing internal
/// <see cref="Reconciler.MountNavigationHost"/> /
/// <see cref="Reconciler.UpdateNavigationHost"/> bodies, which own the
/// per-instance route / cache / transition state tracked in the reconciler's
/// <c>_navigationHostNodes</c> table. <c>Children = null</c> because the
/// delegate body fully owns child mount/swap inside the host Grid.</para>
///
/// <para><b>Unmount parity:</b> teardown stays on the flag-independent
/// <c>UnmountRecursive</c> intercept (Reconciler.cs), which matches the
/// tracked node and tears down its current child before the V1 unmount arm
/// is reached. Cleanup is therefore byte-identical V1 ON ≡ V1 OFF, so this
/// handler intentionally does not override <c>Unmount</c>. The defensive
/// "lost tracking" remount inside <see cref="Reconciler.UpdateNavigationHost"/>
/// is unreachable under normal V1 operation (the node is always present after
/// <c>Mount</c>), so the void <see cref="Update"/> here — which cannot
/// substitute the control — preserves behavior.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class NavigationHostHandler : IElementHandler<NavigationHostElement, WinUI.Grid>
{
    public WinUI.Grid Mount(MountContext ctx, NavigationHostElement el)
        => ctx.Reconciler.MountNavigationHost(el, ctx.RequestRerender);

    public void Update(UpdateContext ctx, NavigationHostElement oldEl, NavigationHostElement newEl, WinUI.Grid ctrl)
        => ctx.Reconciler.UpdateNavigationHost(oldEl, newEl, ctrl, ctx.RequestRerender);

    public ChildrenStrategy<NavigationHostElement, WinUI.Grid>? Children => null;
}
