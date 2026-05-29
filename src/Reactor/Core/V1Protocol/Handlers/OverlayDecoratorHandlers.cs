using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 4 (§4.0.1) — overlay / dialog V1 handlers.
//
// All seven overlay elements (ContentDialog, Flyout, MenuBar, CommandBar,
// MenuFlyout, Popup, CommandBarFlyout) route through V1 via decorator-style
// handlers that now OWN their mount/update logic via the V1-owned
// <see cref="OverlayLifecycle"/> strategy (genuine port). The legacy
// Reconciler.MountXxx/UpdateXxx members are thin delegators to the same
// strategy, so V1 ON ≡ V1 OFF is byte-identical and §4.5 can delete the
// legacy delegators + the V1-OFF switch arms without touching this logic.
//
// Why ContinueDefaultTraversal for every overlay: when the V1 flag is OFF the
// engine SKIPS the V1 unmount arm entirely and runs the type-based recursion
// in UnmountRecursive directly. ContinueDefaultTraversal tells the engine to
// fall through to that SAME recursion after the (no-op) handler Unmount body,
// keeping unmount byte-identical V1 ON ≡ V1 OFF. Reworking overlay teardown
// (closing/detaching the side object) is deferred to §4.5 where it can change
// for the V1-only world without breaking the parity bar.
//
// The three target-wrapping decorators (Flyout, MenuFlyout, CommandBarFlyout)
// return their Target's mounted control; the strategy may return null when the
// Target mounts to nothing (no attachable target) — the null-forgiving
// operator preserves that (the engine installs null in the slot, same as
// V1 OFF). Update returns the strategy result when non-null (a Target type-swap
// substitutes the control) and otherwise keeps the existing control.

/// <summary>§4.0.1 — ContentDialog (modal placeholder + async ShowAsync).</summary>
internal sealed class ContentDialogHandler : IDecoratorElementHandler<ContentDialogElement>
{
    public UIElement Mount(MountContext ctx, ContentDialogElement el)
        => OverlayLifecycle.MountContentDialog(ctx.Reconciler, el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, ContentDialogElement oldEl, ContentDialogElement newEl, UIElement control)
        => OverlayLifecycle.UpdateContentDialog(ctx.Reconciler, oldEl, newEl, (FrameworkElement)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, ContentDialogElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§4.0.1 — Flyout (target-wrapping decorator).</summary>
internal sealed class FlyoutHandler : IDecoratorElementHandler<FlyoutElement>
{
    public UIElement Mount(MountContext ctx, FlyoutElement el)
        => OverlayLifecycle.MountFlyout(ctx.Reconciler, el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, FlyoutElement oldEl, FlyoutElement newEl, UIElement control)
        => OverlayLifecycle.UpdateFlyoutElement(ctx.Reconciler, oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, FlyoutElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§4.0.1 — MenuBar (normal control; plain-WinUI menu items).</summary>
internal sealed class MenuBarHandler : IDecoratorElementHandler<MenuBarElement>
{
    public UIElement Mount(MountContext ctx, MenuBarElement el)
        => OverlayLifecycle.MountMenuBar(ctx.Reconciler, el);

    public UIElement Update(UpdateContext ctx, MenuBarElement oldEl, MenuBarElement newEl, UIElement control)
        => OverlayLifecycle.UpdateMenuBar(ctx.Reconciler, oldEl, newEl, (WinUI.MenuBar)control) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, MenuBarElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§4.0.1 — CommandBar (normal control; Content is a Reactor child).</summary>
internal sealed class CommandBarHandler : IDecoratorElementHandler<CommandBarElement>
{
    public UIElement Mount(MountContext ctx, CommandBarElement el)
        => OverlayLifecycle.MountCommandBar(ctx.Reconciler, el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, CommandBarElement oldEl, CommandBarElement newEl, UIElement control)
        => OverlayLifecycle.UpdateCommandBar(ctx.Reconciler, oldEl, newEl, (WinUI.CommandBar)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CommandBarElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§4.0.1 — MenuFlyout (target-wrapping decorator).</summary>
internal sealed class MenuFlyoutHandler : IDecoratorElementHandler<MenuFlyoutElement>
{
    public UIElement Mount(MountContext ctx, MenuFlyoutElement el)
        => OverlayLifecycle.MountMenuFlyout(ctx.Reconciler, el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, MenuFlyoutElement oldEl, MenuFlyoutElement newEl, UIElement control)
        => OverlayLifecycle.UpdateMenuFlyout(ctx.Reconciler, oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, MenuFlyoutElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§4.0.1 — Popup (StackPanel wrapper hosting a WinUI Popup).</summary>
internal sealed class PopupHandler : IDecoratorElementHandler<PopupElement>
{
    public UIElement Mount(MountContext ctx, PopupElement el)
        => OverlayLifecycle.MountPopup(ctx.Reconciler, el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, PopupElement oldEl, PopupElement newEl, UIElement control)
        => OverlayLifecycle.UpdatePopup(ctx.Reconciler, oldEl, newEl, (WinUI.StackPanel)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, PopupElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§4.0.1 — CommandBarFlyout (target-wrapping decorator).</summary>
internal sealed class CommandBarFlyoutHandler : IDecoratorElementHandler<CommandBarFlyoutElement>
{
    public UIElement Mount(MountContext ctx, CommandBarFlyoutElement el)
        => OverlayLifecycle.MountCommandBarFlyout(ctx.Reconciler, el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, CommandBarFlyoutElement oldEl, CommandBarFlyoutElement newEl, UIElement control)
        => OverlayLifecycle.UpdateCommandBarFlyout(ctx.Reconciler, oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CommandBarFlyoutElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
