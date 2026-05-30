using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// Spec 047 §14 Phase 4 (§4.0.1) — V1-owned modal-lifecycle logic for the seven
// overlay / dialog elements (ContentDialog, Flyout, Popup, MenuBar, MenuFlyout,
// CommandBar, CommandBarFlyout).
//
// These controls are control-side-mounted (modal lifecycle), not
// parent-tree-mounted: the value returned to the engine is a placeholder
// (ContentDialog), a wrapper (Popup), or the Target's own control (the three
// flyouts), while the real overlay content lives in a side-owned slot
// (ContentDialog.Content / Flyout.Content / Popup.Child / menu Items /
// command bar Primary+SecondaryCommands).
//
// Ownership model (genuine port per §4.0.1): the orchestration lives HERE and
// is owned by the V1 layer. Both dispatch paths reach the same implementation:
//   • V1 ON  — the decorator handlers in Handlers/OverlayDecoratorHandlers.cs
//              call straight into these methods.
//   • V1 OFF — the legacy Reconciler.MountXxx/UpdateXxx members are now thin
//              delegators to these methods (transitional bridge).
// This makes the two paths byte-identical and lets §4.5 delete the legacy
// delegators + the V1-OFF switch arms without touching this logic.
//
// Unmount is intentionally NOT ported here: both paths return
// V1UnmountDisposition.ContinueDefaultTraversal so the engine's type-based
// unmount recursion runs identically V1 ON ≡ V1 OFF. Reworking overlay teardown
// (closing/detaching the side object) is deferred to §4.5 where it can change
// for the V1-only world without breaking the parity bar.
internal static class OverlayLifecycle
{
    // ── ContentDialog ───────────────────────────────────────────────────

    public static UIElement MountContentDialog(Reconciler reconciler, ContentDialogElement cdEl, Action requestRerender)
    {
        var placeholder = new WinUI.StackPanel { Visibility = Visibility.Collapsed };
        Reconciler.SetElementTag(placeholder, cdEl);
        if (cdEl.IsOpen) ShowContentDialog(reconciler, cdEl, placeholder, requestRerender);
        return placeholder;
    }

    public static UIElement? UpdateContentDialog(Reconciler reconciler, ContentDialogElement o, ContentDialogElement n, FrameworkElement fe, Action requestRerender)
    {
        if (n.IsOpen && !o.IsOpen) ShowContentDialog(reconciler, n, fe, requestRerender);
        Reconciler.SetElementTag(fe, n);
        return null;
    }

    private static void ShowContentDialog(Reconciler reconciler, ContentDialogElement cdEl, FrameworkElement anchor, Action requestRerender)
    {
        // Source XamlRoot from the placeholder so the dialog routes to the
        // window that owns the anchor. If the anchor isn't attached yet
        // (mount-time IsOpen=true) defer via Loaded — falling back to
        // PrimaryWindow here would misroute the dialog when the anchor lives
        // in a secondary window.
        if (anchor.XamlRoot is null)
        {
            void OnLoaded(object sender, RoutedEventArgs _)
            {
                anchor.Loaded -= OnLoaded;
                // Re-read the current element from the anchor's Tag in case
                // IsOpen was toggled back to false (or the element was
                // replaced) before Loaded fired.
                if (Reconciler.GetElementTag(anchor) is not ContentDialogElement current || !current.IsOpen)
                    return;
                var deferredRoot = anchor.XamlRoot
                    ?? ReactorApp.PrimaryWindow?.NativeWindow.Content?.XamlRoot;
                ShowContentDialogCore(reconciler, current, deferredRoot, requestRerender);
            }
            anchor.Loaded += OnLoaded;
            return;
        }
        ShowContentDialogCore(reconciler, cdEl, anchor.XamlRoot, requestRerender);
    }

    private static async void ShowContentDialogCore(Reconciler reconciler, ContentDialogElement cdEl, XamlRoot? xamlRoot, Action requestRerender)
    {
        var dialog = new WinUI.ContentDialog
        {
            Title = cdEl.Title, PrimaryButtonText = cdEl.PrimaryButtonText,
            DefaultButton = cdEl.DefaultButton,
            IsPrimaryButtonEnabled = cdEl.IsPrimaryButtonEnabled,
            IsSecondaryButtonEnabled = cdEl.IsSecondaryButtonEnabled,
        };
        if (cdEl.SecondaryButtonText is not null) dialog.SecondaryButtonText = cdEl.SecondaryButtonText;
        if (cdEl.CloseButtonText is not null) dialog.CloseButtonText = cdEl.CloseButtonText;
        dialog.Content = reconciler.Mount(cdEl.Content, requestRerender);
        if (xamlRoot is not null) dialog.XamlRoot = xamlRoot;
        if (cdEl.OnOpened is not null) dialog.Opened += (_, _) => cdEl.OnOpened?.Invoke();
        // ApplySetters last so caller .Set(...) wins (including overriding XamlRoot).
        Reconciler.ApplySetters(cdEl.Setters, dialog);
        var winUiResult = await dialog.ShowAsync();
        // FOLLOW-UP (PR #455 nit): this captures the mount-time cdEl directly,
        // whereas Flyout/Popup route their handlers through the live Tag. A
        // dialog can be re-rendered while open, so a later render's OnClosed
        // would not be observed here. Relocated, pre-existing behavior (not a
        // regression) — but this is now the V1-owned home for it, so routing
        // OnClosed through Reconciler.GetElementTag(anchor) for consistency is
        // worth a dedicated follow-up.
        cdEl.OnClosed?.Invoke(winUiResult);
    }

    // ── Flyout ──────────────────────────────────────────────────────────

    public static UIElement? MountFlyout(Reconciler reconciler, FlyoutElement flyEl, Action requestRerender)
    {
        var target = reconciler.Mount(flyEl.Target, requestRerender);
        if (target is FrameworkElement targetFe)
        {
            var flyoutContent = reconciler.Mount(flyEl.FlyoutContent, requestRerender);
            var flyout = new WinUI.Flyout
            {
                Content = flyoutContent,
                Placement = flyEl.Placement,
                ShowMode = flyEl.ShowMode,
                AreOpenCloseAnimationsEnabled = flyEl.AreOpenCloseAnimationsEnabled,
            };
            if (flyEl.OverlayInputPassThroughElement is not null
                && reconciler.Mount(flyEl.OverlayInputPassThroughElement, requestRerender) is DependencyObject pt)
                flyout.OverlayInputPassThroughElement = pt;
            Reconciler.SetElementTag(targetFe, flyEl);
            // Route handlers through the target's Tag so Update() refreshing the tag to the
            // new FlyoutElement causes subsequent Opened/Closed to fire the current delegates —
            // capturing flyEl directly would freeze handlers to the mount-time element.
            if (flyEl.OnOpened is not null)
                flyout.Opened += (_, _) => (Reconciler.GetElementTag(targetFe) as FlyoutElement)?.OnOpened?.Invoke();
            if (flyEl.OnClosed is not null)
                flyout.Closed += (_, _) => (Reconciler.GetElementTag(targetFe) as FlyoutElement)?.OnClosed?.Invoke();
            // SetFlyoutOnControl wires .Flyout on Button/SplitButton targets so
            // clicking opens the flyout natively; non-button targets fall back
            // to SetAttachedFlyout metadata (opened only via ShowAttachedFlyout).
            reconciler.SetFlyoutOnControl(targetFe, flyout);
            Reconciler.ApplySetters(flyEl.Setters, flyout);
            if (flyEl.IsOpen) WinPrim.FlyoutBase.ShowAttachedFlyout(targetFe);
        }
        return target;
    }

    public static UIElement? UpdateFlyoutElement(Reconciler reconciler, FlyoutElement o, FlyoutElement n, UIElement targetControl, Action requestRerender)
    {
        UIElement? updated = targetControl;
        if (reconciler.CanUpdate(o.Target, n.Target))
        {
            var replacement = reconciler.Update(o.Target, n.Target, targetControl, requestRerender);
            if (replacement is not null) updated = replacement;
        }
        else
        {
            reconciler.UnmountChild(targetControl);
            updated = reconciler.Mount(n.Target, requestRerender);
        }

        if (updated is FrameworkElement targetFe)
        {
            Reconciler.SetElementTag(targetFe, n);
            WinPrim.FlyoutBase? existingFlyout = targetFe switch
            {
                WinUI.SplitButton sb => sb.Flyout,
                WinUI.Button btn => btn.Flyout,
                _ => WinPrim.FlyoutBase.GetAttachedFlyout(targetFe),
            };

            if (existingFlyout is WinUI.Flyout flyout)
            {
                if (flyout.Content is UIElement existingContent && reconciler.CanUpdate(o.FlyoutContent, n.FlyoutContent))
                {
                    var contentRepl = reconciler.Update(o.FlyoutContent, n.FlyoutContent, existingContent, requestRerender);
                    if (contentRepl is not null) flyout.Content = contentRepl;
                }
                else
                {
                    if (flyout.Content is UIElement stale) reconciler.UnmountChild(stale);
                    flyout.Content = reconciler.Mount(n.FlyoutContent, requestRerender);
                }
                flyout.Placement = n.Placement;
                if (flyout.ShowMode != n.ShowMode) flyout.ShowMode = n.ShowMode;
                if (flyout.AreOpenCloseAnimationsEnabled != n.AreOpenCloseAnimationsEnabled)
                    flyout.AreOpenCloseAnimationsEnabled = n.AreOpenCloseAnimationsEnabled;
                if (o.OnOpened is null && n.OnOpened is not null)
                {
                    var openedTarget = targetFe;
                    flyout.Opened += (_, _) => (Reconciler.GetElementTag(openedTarget) as FlyoutElement)?.OnOpened?.Invoke();
                }
                if (o.OnClosed is null && n.OnClosed is not null)
                {
                    var closedTarget = targetFe;
                    flyout.Closed += (_, _) => (Reconciler.GetElementTag(closedTarget) as FlyoutElement)?.OnClosed?.Invoke();
                }
                Reconciler.ApplySetters(n.Setters, flyout);
            }
            else
            {
                // No existing flyout or type mismatch — create fresh.
                var flyoutContent = reconciler.Mount(n.FlyoutContent, requestRerender);
                var newFlyout = new WinUI.Flyout
                {
                    Content = flyoutContent,
                    Placement = n.Placement,
                    ShowMode = n.ShowMode,
                    AreOpenCloseAnimationsEnabled = n.AreOpenCloseAnimationsEnabled,
                };
                // Route handlers through the target's Tag (already set to n above) so future
                // Update() calls that refresh the tag keep Opened/Closed pointing at the
                // current FlyoutElement's delegates.
                var handlerTarget = targetFe;
                newFlyout.Opened += (_, _) => (Reconciler.GetElementTag(handlerTarget) as FlyoutElement)?.OnOpened?.Invoke();
                newFlyout.Closed += (_, _) => (Reconciler.GetElementTag(handlerTarget) as FlyoutElement)?.OnClosed?.Invoke();
                reconciler.SetFlyoutOnControl(targetFe, newFlyout);
                Reconciler.ApplySetters(n.Setters, newFlyout);
            }
            if (n.IsOpen && !o.IsOpen) WinPrim.FlyoutBase.ShowAttachedFlyout(targetFe);
        }
        return updated == targetControl ? null : updated;
    }

    // ── MenuBar ─────────────────────────────────────────────────────────

    public static WinUI.MenuBar MountMenuBar(Reconciler reconciler, MenuBarElement mbEl)
    {
        var menuBar = new WinUI.MenuBar();
        foreach (var menuItem in mbEl.Items)
        {
            var mbi = new WinUI.MenuBarItem { Title = menuItem.Title };
            foreach (var flyoutItem in menuItem.Items) mbi.Items.Add(Reconciler.CreateMenuFlyoutItem(flyoutItem));
            menuBar.Items.Add(mbi);
        }
        Reconciler.ApplySetters(mbEl.Setters, menuBar);
        return menuBar;
    }

    public static UIElement? UpdateMenuBar(Reconciler reconciler, MenuBarElement o, MenuBarElement n, WinUI.MenuBar mb)
    {
        int oldCount = o.Items.Length;
        int newCount = n.Items.Length;
        int shared = global::System.Math.Min(oldCount, newCount);

        // Patch shared top-level menus
        for (int i = 0; i < shared; i++)
        {
            var mbi = (WinUI.MenuBarItem)mb.Items[i];
            if (o.Items[i].Title != n.Items[i].Title)
                mbi.Title = n.Items[i].Title;
            reconciler.UpdateMenuFlyoutItems(mbi.Items, o.Items[i].Items, n.Items[i].Items);
        }

        // Remove excess top-level menus
        for (int i = oldCount - 1; i >= shared; i--)
            mb.Items.RemoveAt(i);

        // Add new top-level menus
        for (int i = shared; i < newCount; i++)
        {
            var mbi = new WinUI.MenuBarItem { Title = n.Items[i].Title };
            foreach (var item in n.Items[i].Items)
                mbi.Items.Add(Reconciler.CreateMenuFlyoutItem(item));
            mb.Items.Add(mbi);
        }

        Reconciler.ApplySetters(n.Setters, mb);
        return null;
    }

    // ── CommandBar ──────────────────────────────────────────────────────

    public static WinUI.CommandBar MountCommandBar(Reconciler reconciler, CommandBarElement cmdEl, Action requestRerender)
    {
        var commandBar = new WinUI.CommandBar
        {
            DefaultLabelPosition = cmdEl.DefaultLabelPosition,
            IsOpen = cmdEl.IsOpen,
        };
        if (cmdEl.Content is not null) commandBar.Content = reconciler.Mount(cmdEl.Content, requestRerender);
        if (cmdEl.PrimaryCommands is not null)
            foreach (var cmd in cmdEl.PrimaryCommands) commandBar.PrimaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
        if (cmdEl.SecondaryCommands is not null)
            foreach (var cmd in cmdEl.SecondaryCommands) commandBar.SecondaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
        Reconciler.SetElementTag(commandBar, cmdEl);
        Reconciler.ApplySetters(cmdEl.Setters, commandBar);
        return commandBar;
    }

    public static UIElement? UpdateCommandBar(Reconciler reconciler, CommandBarElement o, CommandBarElement n, WinUI.CommandBar cb, Action requestRerender)
    {
        cb.DefaultLabelPosition = n.DefaultLabelPosition;
        cb.IsOpen = n.IsOpen;

        // Update primary commands in-place
        Reconciler.UpdateAppBarItems(cb.PrimaryCommands, n.PrimaryCommands);
        Reconciler.UpdateAppBarItems(cb.SecondaryCommands, n.SecondaryCommands);

        reconciler.ReconcileChild(o.Content, n.Content,
            () => cb.Content as UIElement,
            c => cb.Content = c,
            () => cb.Content = null,
            requestRerender);

        Reconciler.SetElementTag(cb, n);
        Reconciler.ApplySetters(n.Setters, cb);
        return null;
    }

    // ── MenuFlyout ──────────────────────────────────────────────────────

    public static UIElement? MountMenuFlyout(Reconciler reconciler, MenuFlyoutElement mfEl, Action requestRerender)
    {
        var target = reconciler.Mount(mfEl.Target, requestRerender);
        if (target is FrameworkElement targetFe)
        {
            var menuFlyout = new WinUI.MenuFlyout();
            foreach (var item in mfEl.Items) menuFlyout.Items.Add(Reconciler.CreateMenuFlyoutItem(item));
            Reconciler.SetElementTag(targetFe, mfEl);
            // Use SetFlyoutOnControl so clicking a Button/SplitButton target opens
            // the flyout via .Flyout; non-button targets fall back to attached-flyout
            // metadata (still requires explicit ShowAttachedFlyout to open).
            reconciler.SetFlyoutOnControl(targetFe, menuFlyout);
            Reconciler.ApplySetters(mfEl.Setters, menuFlyout);
        }
        return target;
    }

    public static UIElement? UpdateMenuFlyout(Reconciler reconciler, MenuFlyoutElement o, MenuFlyoutElement n, UIElement targetControl, Action requestRerender)
    {
        UIElement? updated = targetControl;
        if (reconciler.CanUpdate(o.Target, n.Target))
        {
            var replacement = reconciler.Update(o.Target, n.Target, targetControl, requestRerender);
            if (replacement is not null) updated = replacement;
        }
        else
        {
            reconciler.UnmountChild(targetControl);
            updated = reconciler.Mount(n.Target, requestRerender);
        }

        if (updated is FrameworkElement targetFe)
        {
            Reconciler.SetElementTag(targetFe, n);
            // Retrieve the existing MenuFlyout and update items in place.
            WinPrim.FlyoutBase? existingFlyout = targetFe switch
            {
                WinUI.SplitButton sb => sb.Flyout,
                WinUI.Button btn => btn.Flyout,
                _ => WinPrim.FlyoutBase.GetAttachedFlyout(targetFe),
            };
            if (existingFlyout is WinUI.MenuFlyout mf)
            {
                reconciler.UpdateMenuFlyoutItems(mf.Items, o.Items, n.Items);
                Reconciler.ApplySetters(n.Setters, mf);
            }
            else
            {
                // Flyout type changed or was missing — create fresh.
                var menuFlyout = new WinUI.MenuFlyout();
                foreach (var item in n.Items) menuFlyout.Items.Add(Reconciler.CreateMenuFlyoutItem(item));
                reconciler.SetFlyoutOnControl(targetFe, menuFlyout);
                Reconciler.ApplySetters(n.Setters, menuFlyout);
            }
        }
        return updated == targetControl ? null : updated;
    }

    // ── Popup ───────────────────────────────────────────────────────────

    public static UIElement MountPopup(Reconciler reconciler, PopupElement popup, Action requestRerender)
    {
        // Popup is not a UIElement child, so we wrap it in a StackPanel
        var wrapper = new WinUI.StackPanel();
        var p = new WinPrim.Popup
        {
            IsOpen = popup.IsOpen,
            IsLightDismissEnabled = popup.IsLightDismissEnabled,
            HorizontalOffset = popup.HorizontalOffset,
            VerticalOffset = popup.VerticalOffset,
        };
        var child = reconciler.Mount(popup.Child, requestRerender);
        p.Child = child as UIElement;
        Reconciler.SetElementTag(wrapper, popup);
        p.Opened += (s, _) => (Reconciler.GetElementTag(wrapper) as PopupElement)?.OnOpened?.Invoke();
        p.Closed += (s, _) => (Reconciler.GetElementTag(wrapper) as PopupElement)?.OnClosed?.Invoke();
        Reconciler.ApplySetters(popup.Setters, p);
        wrapper.Children.Add(p);
        return wrapper;
    }

    public static UIElement? UpdatePopup(Reconciler reconciler, PopupElement o, PopupElement n, WinUI.StackPanel wrapper, Action requestRerender)
    {
        // The popup itself is the wrapper's first child. Update its scalar
        // props and reconcile the hosted Child in place so transient popup
        // state (focus, scroll) survives parent re-renders.
        if (wrapper.Children.Count == 0 || wrapper.Children[0] is not WinPrim.Popup popup)
            return reconciler.Mount(n, requestRerender);

        // Retag first so Closed/Opened handlers that resolve callbacks via the
        // wrapper's Tag see the new element's closures.
        Reconciler.SetElementTag(wrapper, n);

        if (popup.IsOpen != n.IsOpen) popup.IsOpen = n.IsOpen;
        if (popup.IsLightDismissEnabled != n.IsLightDismissEnabled) popup.IsLightDismissEnabled = n.IsLightDismissEnabled;
        if (popup.HorizontalOffset != n.HorizontalOffset) popup.HorizontalOffset = n.HorizontalOffset;
        if (popup.VerticalOffset != n.VerticalOffset) popup.VerticalOffset = n.VerticalOffset;

        if (popup.Child is UIElement existing && reconciler.CanUpdate(o.Child, n.Child))
        {
            var replacement = reconciler.Update(o.Child, n.Child, existing, requestRerender);
            if (replacement is not null && !ReferenceEquals(popup.Child, replacement))
                popup.Child = replacement;
        }
        else
        {
            if (popup.Child is UIElement stale) reconciler.UnmountChild(stale);
            popup.Child = reconciler.Mount(n.Child, requestRerender) as UIElement;
        }

        Reconciler.ApplySetters(n.Setters, popup);
        return null;
    }

    // ── CommandBarFlyout ────────────────────────────────────────────────

    public static UIElement? MountCommandBarFlyout(Reconciler reconciler, CommandBarFlyoutElement cbf, Action requestRerender)
    {
        var target = reconciler.Mount(cbf.Target, requestRerender);
        if (target is FrameworkElement targetFe)
        {
            var flyout = new WinUI.CommandBarFlyout { Placement = cbf.Placement };
            if (cbf.PrimaryCommands is not null)
                foreach (var cmd in cbf.PrimaryCommands) flyout.PrimaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
            if (cbf.SecondaryCommands is not null)
                foreach (var cmd in cbf.SecondaryCommands) flyout.SecondaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
            Reconciler.SetElementTag(targetFe, cbf);
            WinPrim.FlyoutBase.SetAttachedFlyout(targetFe, flyout);
            Reconciler.ApplySetters(cbf.Setters, flyout);
        }
        return target;
    }

    public static UIElement? UpdateCommandBarFlyout(Reconciler reconciler, CommandBarFlyoutElement o, CommandBarFlyoutElement n, UIElement targetControl, Action requestRerender)
    {
        // Reconcile the target in place and reuse the attached flyout when
        // possible — re-attaching a brand-new flyout on every update would
        // close an already-open flyout and discard its transient state.
        UIElement? updated = targetControl;
        if (reconciler.CanUpdate(o.Target, n.Target))
        {
            var replacement = reconciler.Update(o.Target, n.Target, targetControl, requestRerender);
            if (replacement is not null) updated = replacement;
        }
        else
        {
            reconciler.UnmountChild(targetControl);
            updated = reconciler.Mount(n.Target, requestRerender);
        }

        if (updated is FrameworkElement targetFe)
        {
            Reconciler.SetElementTag(targetFe, n);
            var existing = WinPrim.FlyoutBase.GetAttachedFlyout(targetFe) as WinUI.CommandBarFlyout;
            var commandsChanged =
                !ReferenceEquals(o.PrimaryCommands, n.PrimaryCommands) ||
                !ReferenceEquals(o.SecondaryCommands, n.SecondaryCommands);

            if (existing is null)
            {
                var flyout = new WinUI.CommandBarFlyout { Placement = n.Placement };
                if (n.PrimaryCommands is not null)
                    foreach (var cmd in n.PrimaryCommands) flyout.PrimaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
                if (n.SecondaryCommands is not null)
                    foreach (var cmd in n.SecondaryCommands) flyout.SecondaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
                WinPrim.FlyoutBase.SetAttachedFlyout(targetFe, flyout);
                Reconciler.ApplySetters(n.Setters, flyout);
            }
            else
            {
                if (existing.Placement != n.Placement) existing.Placement = n.Placement;
                if (commandsChanged)
                {
                    existing.PrimaryCommands.Clear();
                    existing.SecondaryCommands.Clear();
                    if (n.PrimaryCommands is not null)
                        foreach (var cmd in n.PrimaryCommands) existing.PrimaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
                    if (n.SecondaryCommands is not null)
                        foreach (var cmd in n.SecondaryCommands) existing.SecondaryCommands.Add(Reconciler.CreateAppBarItem(cmd));
                }
                Reconciler.ApplySetters(n.Setters, existing);
            }
        }
        return updated == targetControl ? null : updated;
    }
}
