using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;

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
// Why most overlays still return ContinueDefaultTraversal: their returned
// control (a Target control, a CommandBar, a MenuBar, or a wrapper StackPanel)
// is torn down by the generic type-based recursion in UnmountRecursive, and
// their menu items / commands are imperative WinUI data (no Reactor subtree).
// Two overlays additionally OWN side-mounted Reactor subtrees that the generic
// recursion cannot reach, so their Unmount bodies tear those down explicitly
// (still returning ContinueDefaultTraversal so the visual subtree teardown
// runs):
//   • Flyout — flyout.Content + flyout.OverlayInputPassThroughElement live on
//     the side flyout object attached to the Target, not under the Target's
//     visual tree; the handler unmounts both and detaches the flyout.
//   • Popup — the wrapper StackPanel hosts a WinUI Popup whose Child is a
//     Reactor subtree the type switch has no branch for; the handler unmounts
//     the child and closes the otherwise-orphaned popup.
//   • ContentDialog — the collapsed placeholder's dialog holds Content as a
//     Reactor subtree with no visual link back; the handler takes the dialog
//     from OverlayLifecycle's live-dialog table, unmounts the content, and
//     hides the otherwise-orphaned dialog.
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
    {
        // §4.5: an open dialog's Content is a Reactor subtree hung off the side
        // WinUI ContentDialog object, NOT a visual child of the placeholder, so
        // the generic UnmountRecursive recursion never reaches it — and the
        // dialog itself would stay on screen after its owner left the tree.
        // Take ownership of the tracked dialog (which makes the ShowAsync
        // continuation's own cleanup a no-op), then clear the placeholder tag
        // before hiding so the tag-routed OnClosed does not spuriously fire
        // during teardown — the same guard PopupHandler uses.
        if (control is FrameworkElement placeholder
            && OverlayLifecycle.TryTakeLiveContentDialog(placeholder) is { } dialog)
        {
            Reconciler.ClearElementTag(placeholder);
            if (dialog.Content is UIElement content)
            {
                dialog.Content = null;
                ctx.Reconciler.UnmountChild(content);
            }
            dialog.Hide();
        }
        return V1UnmountDisposition.ContinueDefaultTraversal;
    }
}

/// <summary>§4.0.1 — Flyout (target-wrapping decorator).</summary>
internal sealed class FlyoutHandler : IDecoratorElementHandler<FlyoutElement>
{
    public UIElement Mount(MountContext ctx, FlyoutElement el)
        => OverlayLifecycle.MountFlyout(ctx.Reconciler, el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, FlyoutElement oldEl, FlyoutElement newEl, UIElement control)
        => OverlayLifecycle.UpdateFlyoutElement(ctx.Reconciler, oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, FlyoutElement? element, UIElement control)
    {
        // §4.5: the flyout's Content + OverlayInputPassThroughElement are Reactor
        // subtrees hung off the side flyout object (attached to the Target), NOT
        // in the Target control's visual child tree — the generic UnmountRecursive
        // recursion never reaches them, so their child component cleanups would
        // leak. Tear them down here, then detach the flyout from the Target so a
        // pooled/reused Target retains no stale flyout state. Keep
        // ContinueDefaultTraversal so the Target subtree still tears down.
        if (control is FrameworkElement targetFe)
        {
            if (Reconciler.GetFlyoutOnControl(targetFe) is WinUI.Flyout flyout)
            {
                if (flyout.Content is UIElement content)
                    ctx.Reconciler.UnmountChild(content);
                flyout.Content = null;
                if (flyout.OverlayInputPassThroughElement is UIElement passThrough)
                    ctx.Reconciler.UnmountChild(passThrough);
                flyout.OverlayInputPassThroughElement = null;
            }

            Reconciler.ClearFlyoutOnControl(targetFe);
        }
        return V1UnmountDisposition.ContinueDefaultTraversal;
    }
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
    {
        // §4.5: the wrapper StackPanel hosts a WinUI Popup whose Child is a
        // Reactor subtree. UnmountRecursive recurses the wrapper's children and
        // reaches the Popup, but Popup has no branch in the generic type switch,
        // so popup.Child (and its component cleanups) would leak. Tear it down
        // here and close the otherwise-orphaned free-floating popup. Clear the
        // wrapper tag before closing so the Closed handler — which resolves
        // OnClosed via the tag — does not spuriously fire during teardown.
        if (control is WinUI.StackPanel wrapper
            && wrapper.Children.OfType<WinPrim.Popup>().FirstOrDefault() is { } popup)
        {
            if (popup.Child is UIElement popupChild)
                ctx.Reconciler.UnmountChild(popupChild);
            popup.Child = null;
            if (popup.IsOpen)
            {
                Reconciler.ClearElementTag(wrapper);
                popup.IsOpen = false;
            }
        }
        return V1UnmountDisposition.ContinueDefaultTraversal;
    }
}

/// <summary>§4.0.1 — CommandBarFlyout (target-wrapping decorator).</summary>
internal sealed class CommandBarFlyoutHandler : IDecoratorElementHandler<CommandBarFlyoutElement>
{
    public UIElement Mount(MountContext ctx, CommandBarFlyoutElement el)
        => OverlayLifecycle.MountCommandBarFlyout(ctx.Reconciler, el, ctx.RequestRerender)!;

    public UIElement Update(UpdateContext ctx, CommandBarFlyoutElement oldEl, CommandBarFlyoutElement newEl, UIElement control)
        => OverlayLifecycle.UpdateCommandBarFlyout(ctx.Reconciler, oldEl, newEl, control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CommandBarFlyoutElement? element, UIElement control)
    {
        // Detach the flyout so a pooled/reused target can't keep showing the previous
        // component's command bar. ElementPool.CleanElement already nulls Button.Flyout,
        // but nothing clears the attached-flyout slot a non-button target uses.
        if (control is FrameworkElement targetFe
            && Reconciler.GetFlyoutOnControl(targetFe) is WinUI.CommandBarFlyout)
        {
            Reconciler.ClearFlyoutOnControl(targetFe);
        }
        return V1UnmountDisposition.ContinueDefaultTraversal;
    }
}
