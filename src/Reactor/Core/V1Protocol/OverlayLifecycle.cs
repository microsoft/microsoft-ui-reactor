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

    // Back-reference from the collapsed placeholder to the live WinUI dialog.
    // ContentDialog is side-mounted — dialog.Content is a Reactor subtree owned
    // by the dialog object, not a visual child of the placeholder — and unlike
    // Flyout (Reconciler.GetFlyoutOnControl) or Popup (wrapper.Children[0])
    // there is no slot to read the side object back from. Without this, update
    // and unmount cannot reach the dialog at all: open content never
    // re-renders, a declared IsOpen = false never closes it, and an unmounted
    // owner leaves the dialog on screen with its subtree leaked. Keyed weakly
    // by the placeholder, so entries die with it.
    //
    // A side table rather than a ReactorState slot, matching how every other
    // rare, feature-local native association is stored (Reconciler._dndStates,
    // Reconciler._gestureStates, Reconciler.s_inlineUiExtentPins): ReactorState
    // is allocated for every native element in the tree, and its slots are all
    // Reactor abstractions rather than concrete WinUI control types. StackPanel
    // is poolable, so the placeholder can be recycled into an unrelated element
    // — ContentDialogHandler.Unmount takes the entry back out during the V1
    // unmount dispatch, which runs before the control reaches the pool.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, WinUI.ContentDialog> s_liveDialogs = new();

    public static UIElement MountContentDialog(Reconciler reconciler, ContentDialogElement cdEl, Action requestRerender)
    {
        var placeholder = new WinUI.StackPanel { Visibility = Visibility.Collapsed };
        Reconciler.SetElementTag(placeholder, cdEl);
        if (cdEl.IsOpen) ShowContentDialog(reconciler, cdEl, placeholder, requestRerender);
        return placeholder;
    }

    public static UIElement? UpdateContentDialog(Reconciler reconciler, ContentDialogElement o, ContentDialogElement n, FrameworkElement fe, Action requestRerender)
    {
        // Retag first so the Opened/Closed handlers — and the deferred Loaded
        // path — resolve this render's closures instead of the mount-time ones.
        Reconciler.SetElementTag(fe, n);

        if (n.IsOpen && !o.IsOpen)
        {
            ShowContentDialog(reconciler, n, fe, requestRerender);
            return null;
        }

        // No live dialog: never opened, still deferred waiting on XamlRoot, or
        // already dismissed. A rising edge above is the only way back.
        if (!s_liveDialogs.TryGetValue(fe, out var dialog)) return null;

        if (!n.IsOpen && o.IsOpen)
        {
            // Falling edge — honor the declared IsOpen. Hide() drives ShowAsync's
            // continuation, which unmounts the content and dispatches OnClosed,
            // exactly as a user dismissal does (and matching Popup, whose
            // IsOpen = false likewise raises Closed).
            dialog.Hide();
            return null;
        }

        SyncContentDialogProps(dialog, n);

        // Reconcile the side-mounted content in place so transient state inside
        // the dialog (focus, scroll, caret) survives the owner's re-renders.
        if (dialog.Content is UIElement existing && reconciler.CanUpdate(o.Content, n.Content))
        {
            var replacement = reconciler.Update(o.Content, n.Content, existing, requestRerender);
            if (replacement is not null && !ReferenceEquals(dialog.Content, replacement))
                dialog.Content = replacement;
        }
        else
        {
            if (dialog.Content is UIElement stale) reconciler.UnmountChild(stale);
            dialog.Content = reconciler.Mount(n.Content, requestRerender) as UIElement;
        }

        Reconciler.ApplySetters(n.Setters, dialog);
        return null;
    }

    private static void SyncContentDialogProps(WinUI.ContentDialog dialog, ContentDialogElement n)
    {
        if (dialog.Title as string != n.Title) dialog.Title = n.Title;
        if (dialog.PrimaryButtonText != n.PrimaryButtonText) dialog.PrimaryButtonText = n.PrimaryButtonText;
        if (dialog.DefaultButton != n.DefaultButton) dialog.DefaultButton = n.DefaultButton;
        if (dialog.IsPrimaryButtonEnabled != n.IsPrimaryButtonEnabled) dialog.IsPrimaryButtonEnabled = n.IsPrimaryButtonEnabled;
        if (dialog.IsSecondaryButtonEnabled != n.IsSecondaryButtonEnabled) dialog.IsSecondaryButtonEnabled = n.IsSecondaryButtonEnabled;
        // Optional labels converge on null: the element's null means "no such
        // button", so a render that drops one has to clear the text or the
        // button would be unremovable once shown. Normalizing to "" — WinUI's
        // own DP default, and what the mount path leaves behind when it skips
        // the write — keeps Update(o, n) landing exactly where Mount(n) would,
        // and keeps the comparison from thrashing null against "".
        var secondary = n.SecondaryButtonText ?? string.Empty;
        if (dialog.SecondaryButtonText != secondary) dialog.SecondaryButtonText = secondary;
        var close = n.CloseButtonText ?? string.Empty;
        if (dialog.CloseButtonText != close) dialog.CloseButtonText = close;
    }

    /// <summary>
    /// Hands the dialog currently showing for <paramref name="anchor"/> to the caller and
    /// drops the tracking entry, transferring ownership so the <c>ShowAsync</c> continuation's
    /// own cleanup becomes a no-op. Returns null when no dialog is showing for the placeholder.
    /// </summary>
    internal static WinUI.ContentDialog? TryTakeLiveContentDialog(FrameworkElement anchor)
    {
        if (!s_liveDialogs.TryGetValue(anchor, out var dialog)) return null;
        s_liveDialogs.Remove(anchor);
        return dialog;
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
                ShowContentDialogCore(reconciler, current, anchor, deferredRoot, requestRerender);
            }
            anchor.Loaded += OnLoaded;
            return;
        }
        ShowContentDialogCore(reconciler, cdEl, anchor, anchor.XamlRoot, requestRerender);
    }

    private static async void ShowContentDialogCore(Reconciler reconciler, ContentDialogElement cdEl, FrameworkElement anchor, XamlRoot? xamlRoot, Action requestRerender)
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
        // Resolve callbacks through the anchor's live Tag the way Flyout/Popup do,
        // rather than capturing the mount-time element: the dialog re-renders
        // while open so those closures go stale, and clearing the tag is how
        // unmount teardown suppresses an OnClosed it did not mean to raise.
        dialog.Opened += (_, _) => (Reconciler.GetElementTag(anchor) as ContentDialogElement)?.OnOpened?.Invoke();
        // ApplySetters last so caller .Set(...) wins (including overriding XamlRoot).
        Reconciler.ApplySetters(cdEl.Setters, dialog);
        // Publish before showing so the very next render can reach the dialog.
        s_liveDialogs.AddOrUpdate(anchor, dialog);
        // True when this call still owns the placeholder's tracking entry at
        // close time. False once unmount teardown has taken it, or once a
        // re-open has installed a newer dialog over it.
        bool ownsClose;
        WinUI.ContentDialogResult winUiResult;
        try
        {
            winUiResult = await dialog.ShowAsync();
        }
        finally
        {
            // In a finally so it also runs when ShowAsync throws — WinUI rejects
            // a second dialog on the same XamlRoot — which would otherwise strand
            // the tracking entry and leak the content subtree mounted above.
            ownsClose = s_liveDialogs.TryGetValue(anchor, out var tracked) && ReferenceEquals(tracked, dialog);
            if (ownsClose) s_liveDialogs.Remove(anchor);
            // Unmount the content: it was mounted at open time and nothing else
            // tears it down, so every open/close cycle would otherwise leak its
            // component cleanups. Already null when teardown took ownership.
            if (dialog.Content is UIElement content)
            {
                dialog.Content = null;
                reconciler.UnmountChild(content);
            }
        }

        // Closed by the user or by a declared IsOpen = false. Resolved through
        // the anchor's live tag so the latest render's closure wins. Gated on
        // ownership: teardown must not raise a close it deliberately suppressed,
        // and a dialog that a re-open replaced must not fire its close against
        // the element now driving the newer dialog.
        if (ownsClose)
            (Reconciler.GetElementTag(anchor) as ContentDialogElement)?.OnClosed?.Invoke(winUiResult);
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
                ShowMode = flyEl.ShowMode,
                AreOpenCloseAnimationsEnabled = flyEl.AreOpenCloseAnimationsEnabled,
            };
            FlyoutPlacement.Apply(flyout, flyEl.Placement);
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
            // Read back from whichever slot SetFlyoutOnControl wrote to.
            var existingFlyout = Reconciler.GetFlyoutOnControl(targetFe);

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
                FlyoutPlacement.Apply(flyout, n.Placement);
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
                    ShowMode = n.ShowMode,
                    AreOpenCloseAnimationsEnabled = n.AreOpenCloseAnimationsEnabled,
                };
                FlyoutPlacement.Apply(newFlyout, n.Placement);
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
            foreach (var flyoutItem in menuItem.Items) mbi.Items.Add(MenuCommandFactory.CreateMenuFlyoutItem(flyoutItem));
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
            MenuCommandFactory.UpdateMenuFlyoutItems(mbi.Items, o.Items[i].Items, n.Items[i].Items);
        }

        // Remove excess top-level menus
        for (int i = oldCount - 1; i >= shared; i--)
            mb.Items.RemoveAt(i);

        // Add new top-level menus
        for (int i = shared; i < newCount; i++)
        {
            var mbi = new WinUI.MenuBarItem { Title = n.Items[i].Title };
            foreach (var item in n.Items[i].Items)
                mbi.Items.Add(MenuCommandFactory.CreateMenuFlyoutItem(item));
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
            foreach (var cmd in cmdEl.PrimaryCommands) commandBar.PrimaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
        if (cmdEl.SecondaryCommands is not null)
            foreach (var cmd in cmdEl.SecondaryCommands) commandBar.SecondaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
        Reconciler.SetElementTag(commandBar, cmdEl);
        Reconciler.ApplySetters(cmdEl.Setters, commandBar);
        return commandBar;
    }

    public static UIElement? UpdateCommandBar(Reconciler reconciler, CommandBarElement o, CommandBarElement n, WinUI.CommandBar cb, Action requestRerender)
    {
        cb.DefaultLabelPosition = n.DefaultLabelPosition;
        cb.IsOpen = n.IsOpen;

        // Update primary commands in-place
        MenuCommandFactory.UpdateAppBarItems(cb.PrimaryCommands, n.PrimaryCommands);
        MenuCommandFactory.UpdateAppBarItems(cb.SecondaryCommands, n.SecondaryCommands);

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
            foreach (var item in mfEl.Items) menuFlyout.Items.Add(MenuCommandFactory.CreateMenuFlyoutItem(item));
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
            // Retrieve the existing MenuFlyout (from whichever slot SetFlyoutOnControl
            // wrote to) and update items in place.
            var existingFlyout = Reconciler.GetFlyoutOnControl(targetFe);
            if (existingFlyout is WinUI.MenuFlyout mf)
            {
                MenuCommandFactory.UpdateMenuFlyoutItems(mf.Items, o.Items, n.Items);
                Reconciler.ApplySetters(n.Setters, mf);
            }
            else
            {
                // Flyout type changed or was missing — create fresh.
                var menuFlyout = new WinUI.MenuFlyout();
                foreach (var item in n.Items) menuFlyout.Items.Add(MenuCommandFactory.CreateMenuFlyoutItem(item));
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
            var flyout = new WinUI.CommandBarFlyout();
            FlyoutPlacement.Apply(flyout, cbf.Placement);
            if (cbf.PrimaryCommands is not null)
                foreach (var cmd in cbf.PrimaryCommands) flyout.PrimaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
            if (cbf.SecondaryCommands is not null)
                foreach (var cmd in cbf.SecondaryCommands) flyout.SecondaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
            Reconciler.SetElementTag(targetFe, cbf);
            // SetFlyoutOnControl (not SetAttachedFlyout) so clicking a Button/SplitButton
            // target opens the flyout natively, matching Flyout and MenuFlyout. WinUI's
            // own AttachedFlyout docs say the same: "To attach a flyout to a Button, use
            // Button.Flyout instead." An attached flyout only ever opens via an explicit
            // ShowAttachedFlyout call, which nothing here makes.
            reconciler.SetFlyoutOnControl(targetFe, flyout);
            Reconciler.ApplySetters(cbf.Setters, flyout);
            if (cbf.IsOpen) ShowFlyoutWhenReady(targetFe, flyout);
        }
        return target;
    }

    public static UIElement? UpdateCommandBarFlyout(Reconciler reconciler, CommandBarFlyoutElement o, CommandBarFlyoutElement n, UIElement targetControl, Action requestRerender)
    {
        // Reconcile the target in place and reuse the installed flyout when
        // possible — re-installing a brand-new flyout on every update would
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
            // Must read from the same slot mount wrote to — an attached-only lookup
            // returns null for a Button target and this would create a duplicate
            // flyout the user can never see while the live one keeps stale commands.
            var existing = Reconciler.GetFlyoutOnControl(targetFe) as WinUI.CommandBarFlyout;
            var commandsChanged =
                !ReferenceEquals(o.PrimaryCommands, n.PrimaryCommands) ||
                !ReferenceEquals(o.SecondaryCommands, n.SecondaryCommands);

            if (existing is null)
            {
                var flyout = new WinUI.CommandBarFlyout();
                FlyoutPlacement.Apply(flyout, n.Placement);
                if (n.PrimaryCommands is not null)
                    foreach (var cmd in n.PrimaryCommands) flyout.PrimaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
                if (n.SecondaryCommands is not null)
                    foreach (var cmd in n.SecondaryCommands) flyout.SecondaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
                reconciler.SetFlyoutOnControl(targetFe, flyout);
                Reconciler.ApplySetters(n.Setters, flyout);
                existing = flyout;
            }
            else
            {
                FlyoutPlacement.Apply(existing, n.Placement);
                if (commandsChanged)
                {
                    existing.PrimaryCommands.Clear();
                    existing.SecondaryCommands.Clear();
                    if (n.PrimaryCommands is not null)
                        foreach (var cmd in n.PrimaryCommands) existing.PrimaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
                    if (n.SecondaryCommands is not null)
                        foreach (var cmd in n.SecondaryCommands) existing.SecondaryCommands.Add(MenuCommandFactory.CreateAppBarItem(cmd));
                }
                Reconciler.ApplySetters(n.Setters, existing);
            }
            if (n.IsOpen && !o.IsOpen) ShowFlyoutWhenReady(targetFe, existing);
        }
        return updated == targetControl ? null : updated;
    }

    /// <summary>
    /// Whether a target's current element still wants its CommandBarFlyout open. Guards the
    /// deferred (Loaded) open against the element being re-rendered with <c>IsOpen = false</c>,
    /// swapped for a different element, or unmounted (the tag is cleared on pool return) in the
    /// window between mount and the target entering the live tree.
    /// </summary>
    internal static bool IsStillRequestingOpen(Element? tag) => tag is CommandBarFlyoutElement { IsOpen: true };

    /// <summary>
    /// Opens <paramref name="flyout"/> against <paramref name="target"/>, deferring to the
    /// target's first <c>Loaded</c> when it isn't in a live tree yet (at mount time it has no
    /// XamlRoot and <c>ShowAt</c> would throw).
    ///
    /// Deliberately not <c>FlyoutBase.ShowAttachedFlyout</c>: that reads the AttachedFlyout
    /// property, and a Button/SplitButton target holds its flyout in the control's own
    /// <c>Flyout</c> slot, so the attached lookup would find nothing and silently no-op.
    /// </summary>
    private static void ShowFlyoutWhenReady(FrameworkElement target, WinPrim.FlyoutBase flyout)
    {
        if (target.IsLoaded)
        {
            flyout.ShowAt(target);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            target.Loaded -= OnLoaded;
            // Only honour a deferred open if this flyout is still the one installed on the
            // target AND the target's current element still asks to be open — otherwise a
            // re-render that cleared IsOpen (or a recycled control) would pop a stale flyout.
            if (ReferenceEquals(Reconciler.GetFlyoutOnControl(target), flyout)
                && IsStillRequestingOpen(Reconciler.GetElementTag(target)))
            {
                flyout.ShowAt(target);
            }
        }

        target.Loaded += OnLoaded;
    }
}
