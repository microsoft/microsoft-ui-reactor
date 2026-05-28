using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 3 prelude — overlay carve closures.
//
// All seven overlay elements (ContentDialog, Flyout, MenuBar, CommandBar,
// MenuFlyout, Popup, CommandBarFlyout) route through V1 via decorator-style
// handlers that delegate Mount/Update to the engine's existing internal
// MountXxx/UpdateXxx bodies and return ContinueDefaultTraversal on unmount.
//
// Why ContinueDefaultTraversal for every overlay: when the V1 flag is OFF the
// engine SKIPS the V1 unmount arm entirely and runs the type-based recursion
// in UnmountRecursive directly. ContinueDefaultTraversal tells the engine to
// fall through to that SAME recursion after the (no-op) handler Unmount body.
// Combined with delegating to the identical legacy Mount/Update bodies, the
// V1 ON path is byte-identical to V1 OFF across mount, update AND unmount —
// including the side-mount semantics of flyout content, popup children and
// dialog content (whatever the legacy path does, this preserves exactly).
//
// The three target-wrapping decorators (Flyout, MenuFlyout, CommandBarFlyout)
// return their Target's mounted control; the legacy body may return null when
// the Target mounts to nothing (no attachable target) — the null-forgiving
// operator preserves that (the engine installs null in the slot, same as
// V1 OFF). Update returns the legacy result when non-null (a Target type-swap
// substitutes the control) and otherwise keeps the existing control.

/// <summary>§14 prelude — ContentDialog (modal placeholder + async ShowAsync).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class ContentDialogHandler : IDecoratorElementHandler<ContentDialogElement>
{
    public UIElement Mount(MountContext ctx, ContentDialogElement el)
        => ctx.Reconciler.MountContentDialog(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, ContentDialogElement oldEl, ContentDialogElement newEl, UIElement control)
        => ctx.Reconciler.UpdateContentDialog(oldEl, newEl, (FrameworkElement)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, ContentDialogElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — Flyout (target-wrapping decorator).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class FlyoutHandler : IDecoratorElementHandler<FlyoutElement>
{
    public UIElement Mount(MountContext ctx, FlyoutElement el)
        => ctx.Reconciler.MountFlyout(el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, FlyoutElement oldEl, FlyoutElement newEl, UIElement control)
        => ctx.Reconciler.UpdateFlyoutElement(oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, FlyoutElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — MenuBar (normal control; plain-WinUI menu items).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class MenuBarHandler : IDecoratorElementHandler<MenuBarElement>
{
    public UIElement Mount(MountContext ctx, MenuBarElement el)
        => ctx.Reconciler.MountMenuBar(el);

    public UIElement Update(UpdateContext ctx, MenuBarElement oldEl, MenuBarElement newEl, UIElement control)
        => ctx.Reconciler.UpdateMenuBar(oldEl, newEl, (WinUI.MenuBar)control) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, MenuBarElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — CommandBar (normal control; Content is a Reactor child).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class CommandBarHandler : IDecoratorElementHandler<CommandBarElement>
{
    public UIElement Mount(MountContext ctx, CommandBarElement el)
        => ctx.Reconciler.MountCommandBar(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, CommandBarElement oldEl, CommandBarElement newEl, UIElement control)
        => ctx.Reconciler.UpdateCommandBar(oldEl, newEl, (WinUI.CommandBar)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CommandBarElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — MenuFlyout (target-wrapping decorator).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class MenuFlyoutHandler : IDecoratorElementHandler<MenuFlyoutElement>
{
    public UIElement Mount(MountContext ctx, MenuFlyoutElement el)
        => ctx.Reconciler.MountMenuFlyout(el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, MenuFlyoutElement oldEl, MenuFlyoutElement newEl, UIElement control)
        => ctx.Reconciler.UpdateMenuFlyout(oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, MenuFlyoutElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — Popup (StackPanel wrapper hosting a WinUI Popup).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class PopupHandler : IDecoratorElementHandler<PopupElement>
{
    public UIElement Mount(MountContext ctx, PopupElement el)
        => ctx.Reconciler.MountPopup(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, PopupElement oldEl, PopupElement newEl, UIElement control)
        => ctx.Reconciler.UpdatePopup(oldEl, newEl, (WinUI.StackPanel)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, PopupElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 prelude — CommandBarFlyout (target-wrapping decorator).</summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class CommandBarFlyoutHandler : IDecoratorElementHandler<CommandBarFlyoutElement>
{
    public UIElement Mount(MountContext ctx, CommandBarFlyoutElement el)
        => ctx.Reconciler.MountCommandBarFlyout(el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, CommandBarFlyoutElement oldEl, CommandBarFlyoutElement newEl, UIElement control)
        => ctx.Reconciler.UpdateCommandBarFlyout(oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CommandBarFlyoutElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
