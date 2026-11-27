using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Flyout attachment and flyout slots.
public sealed partial class Reconciler
{
    /// <summary>
    /// Sets or updates the flyout on a control. On first mount, creates a new flyout.
    /// On update, reconciles the content inside the existing flyout to keep it open.
    /// </summary>
    private void ApplyFlyoutAttachment(FrameworkElement fe, Element? oldFlyoutEl, Element newFlyoutEl, Action requestRerender)
    {
        // Patch the flyout this modifier installed, even when another writer (a button's own
        // flyout: argument, a flyout decorator) has since taken the slot. That writer's flyout is
        // left alone, and the modifier's content stays reconciled until it is released (#1753).
        if (oldFlyoutEl is not null && TryGetReactorState(fe, out var state) && state.ModifierFlyout is { } installed)
        {
            UpdateFlyoutInPlace(installed, oldFlyoutEl, newFlyoutEl, requestRerender);
            return;
        }

        // First mount — create new flyout
        var flyout = CreateFlyoutFromElement(newFlyoutEl, requestRerender);
        // A flyout this modifier installed earlier is being replaced, so its content goes now, and
        // what it displaced stays the slot's fallback.
        var displaced = UnmountFlyoutAttachmentContent(fe, out var earlier) ? earlier : GetFlyoutOnControl(fe);
        if (flyout is null)
        {
            RestoreFlyoutSlot(fe, displaced);
            return;
        }

        SetFlyoutOnControl(fe, flyout);
        var installedState = GetOrCreateReactorState(fe);
        installedState.ModifierFlyout = flyout;
        installedState.ModifierDisplacedFlyout = displaced;
    }

    /// <summary>
    /// Updates the content inside an existing flyout without replacing the flyout object.
    /// This keeps the flyout open while its content changes.
    /// </summary>
    private void UpdateFlyoutInPlace(WinPrim.FlyoutBase existingFlyout, Element oldEl, Element newEl, Action requestRerender)
    {
        // ContentFlyout → reconcile child content inside the existing Flyout
        if (newEl is ContentFlyoutElement newCf && existingFlyout is WinUI.Flyout flyout)
        {
            var oldContent = oldEl is ContentFlyoutElement oldCf ? oldCf.Content : null;
            if (oldContent is not null && flyout.Content is UIElement existingContent && CanUpdate(oldContent, newCf.Content))
            {
                var replacement = UpdateHostedChild(oldContent, newCf.Content, existingContent, requestRerender);
                if (replacement is not null && !ReferenceEquals(flyout.Content, replacement))
                    flyout.Content = replacement;
            }
            else
            {
                // Type changed — remount content
                flyout.Content = Mount(newCf.Content, requestRerender);
            }
            FlyoutPlacement.Apply(flyout, newCf.Placement);
            return;
        }

        // MenuFlyout → recreate items (lightweight, no open-state issue)
        if (newEl is MenuFlyoutContentElement newMf && existingFlyout is WinUI.MenuFlyout menuFlyout)
        {
            menuFlyout.Items.Clear();
            foreach (var item in newMf.Items) menuFlyout.Items.Add(MenuCommandFactory.CreateMenuFlyoutItem(item, this));
            FlyoutPlacement.Apply(menuFlyout, newMf.Placement);
            return;
        }

        // Fallback: plain element → reconcile inside existing Flyout
        if (existingFlyout is WinUI.Flyout plainFlyout && plainFlyout.Content is UIElement existingCtrl)
        {
            if (CanUpdate(oldEl, newEl))
            {
                var replacement = UpdateHostedChild(oldEl, newEl, existingCtrl, requestRerender);
                if (replacement is not null && !ReferenceEquals(plainFlyout.Content, replacement))
                    plainFlyout.Content = replacement;
            }
            else
            {
                plainFlyout.Content = Mount(newEl, requestRerender);
            }
        }
    }

    /// <summary>
    /// Which slot a flyout occupies on a given target control. Button-family targets
    /// get the control's own <c>Flyout</c> property, which WinUI opens on click; every
    /// other target falls back to <c>FlyoutBase.AttachedFlyout</c> metadata, which only
    /// opens via an explicit <c>ShowAttachedFlyout</c> call.
    /// </summary>
    internal enum FlyoutSlot
    {
        Attached,
        Button,
        SplitButton,
    }

    /// <summary>
    /// Single source of truth for the flyout-slot rule shared by
    /// <see cref="SetFlyoutOnControl"/> and <see cref="GetFlyoutOnControl"/>. Writers and
    /// readers that disagree on the slot silently lose the flyout, so both go through here.
    /// </summary>
    internal static FlyoutSlot ResolveFlyoutSlot(Type targetType)
    {
        // Check SplitButton before Button (SplitButton doesn't inherit from Button,
        // but DropDownButton does, so the Button catch-all handles it).
        if (typeof(WinUI.SplitButton).IsAssignableFrom(targetType)) return FlyoutSlot.SplitButton;
        if (typeof(WinUI.Button).IsAssignableFrom(targetType)) return FlyoutSlot.Button;  // AppBarButton, DropDownButton inherit from Button
        return FlyoutSlot.Attached;
    }

    internal void SetFlyoutOnControl(FrameworkElement fe, WinPrim.FlyoutBase flyout)
    {
        switch (ResolveFlyoutSlot(fe.GetType()))
        {
            case FlyoutSlot.SplitButton: ((WinUI.SplitButton)fe).Flyout = flyout; break;
            case FlyoutSlot.Button: ((WinUI.Button)fe).Flyout = flyout; break;
            default: WinPrim.FlyoutBase.SetAttachedFlyout(fe, flyout); break;
        }
    }

    /// <summary>
    /// Reads back the flyout <see cref="SetFlyoutOnControl"/> installed. Update paths must
    /// use this rather than <c>GetAttachedFlyout</c> — a Button/SplitButton target holds its
    /// flyout in the control's own slot, so an attached-only lookup returns null and the
    /// caller creates a duplicate flyout the user can never see.
    /// </summary>
    internal static WinPrim.FlyoutBase? GetFlyoutOnControl(FrameworkElement fe) =>
        ResolveFlyoutSlot(fe.GetType()) switch
        {
            FlyoutSlot.SplitButton => ((WinUI.SplitButton)fe).Flyout,
            FlyoutSlot.Button => ((WinUI.Button)fe).Flyout,
            _ => WinPrim.FlyoutBase.GetAttachedFlyout(fe),
        };

    /// <summary>
    /// Detaches whatever <see cref="SetFlyoutOnControl"/> installed, so a pooled/reused target
    /// retains no stale flyout. Clears the same slot the writer and reader agree on.
    /// </summary>
    internal static void ClearFlyoutOnControl(FrameworkElement fe)
    {
        V1Protocol.OverlayLifecycle.ForgetFlyoutOwner(fe);
        switch (ResolveFlyoutSlot(fe.GetType()))
        {
            case FlyoutSlot.SplitButton: ((WinUI.SplitButton)fe).Flyout = null; break;
            case FlyoutSlot.Button: ((WinUI.Button)fe).Flyout = null; break;
            default: WinPrim.FlyoutBase.SetAttachedFlyout(fe, null); break;
        }
    }

    /// <summary>The dependency property behind <paramref name="fe"/>'s flyout slot.</summary>
    internal static DependencyProperty FlyoutSlotProperty(FrameworkElement fe) =>
        ResolveFlyoutSlot(fe.GetType()) switch
        {
            FlyoutSlot.SplitButton => WinUI.SplitButton.FlyoutProperty,
            FlyoutSlot.Button => WinUI.Button.FlyoutProperty,
            _ => WinPrim.FlyoutBase.AttachedFlyoutProperty,
        };

    /// <summary>
    /// Unmounts and detaches a flyout's Reactor-mounted content. Shared by the
    /// <c>.WithFlyout(...)</c> release and <c>OverlayLifecycle.ReleaseFlyout</c>.
    /// </summary>
    internal void UnmountFlyoutContent(WinUI.Flyout flyout)
    {
        if (flyout.Content is UIElement content) UnmountChild(content);
        flyout.Content = null;
    }

    /// <summary>
    /// Releases the flyout a <c>.WithFlyout(...)</c> modifier installed on
    /// <paramref name="fe"/> (issue #1753): unmounts its content and forgets it. Returns true
    /// when that flyout still occupies the slot, which the caller then hands back to
    /// <paramref name="displaced"/>, the flyout the modifier replaced there (a button's own
    /// <c>flyout:</c> argument, which its descriptor won't write again while it is unchanged).
    /// Another writer, such as a flyout decorator or a changed <c>flyout:</c> argument, may have
    /// replaced the modifier's flyout, and that writer's flyout is left alone.
    /// </summary>
    private bool UnmountFlyoutAttachmentContent(FrameworkElement fe, out WinPrim.FlyoutBase? displaced)
    {
        displaced = null;
        if (!TryGetReactorState(fe, out var state) || state.ModifierFlyout is not { } flyout) return false;
        displaced = state.ModifierDisplacedFlyout;
        state.ModifierFlyout = null;
        state.ModifierDisplacedFlyout = null;
        if (flyout is WinUI.Flyout contentFlyout) UnmountFlyoutContent(contentFlyout);
        return ReferenceEquals(GetFlyoutOnControl(fe), flyout);
    }

    /// <summary>Puts <paramref name="displaced"/> back in the slot, or clears it when there is none.</summary>
    private void RestoreFlyoutSlot(FrameworkElement fe, WinPrim.FlyoutBase? displaced)
    {
        if (displaced is null) fe.ClearValue(FlyoutSlotProperty(fe));
        else SetFlyoutOnControl(fe, displaced);
    }

    /// <summary>
    /// Unmount counterpart of the <c>.WithFlyout(...)</c> unset arm in
    /// <c>ApplyFlyoutModifiers</c>: a control leaving the tree takes its modifier
    /// flyout's content with it, so the content's components run their cleanups and a pooled
    /// control holds no flyout from its previous renter (issue #1753).
    /// </summary>
    private void ReleaseFlyoutAttachment(FrameworkElement fe)
    {
        if (UnmountFlyoutAttachmentContent(fe, out var displaced)) RestoreFlyoutSlot(fe, displaced);
    }

    /// <summary>
    /// Creates a WinUI FlyoutBase from a Reactor element descriptor.
    /// Recognizes ContentFlyoutElement and MenuFlyoutContentElement for configured flyouts,
    /// and falls back to wrapping plain elements in a basic Flyout.
    /// Used by both ApplyModifiers (for .WithFlyout()/.WithContextFlyout()) and
    /// button mount methods (for direct Flyout parameter).
    /// </summary>
    internal WinPrim.FlyoutBase? CreateFlyoutFromElement(Element flyoutEl, Action requestRerender)
    {
        switch (flyoutEl)
        {
            case ContentFlyoutElement cf:
            {
                var content = Mount(cf.Content, requestRerender);
                if (content is null) return null;
                var contentFlyout = new WinUI.Flyout { Content = content };
                FlyoutPlacement.Apply(contentFlyout, cf.Placement);
                return contentFlyout;
            }
            case MenuFlyoutContentElement mf:
            {
                var menuFlyout = new WinUI.MenuFlyout();
                // Placement is routed through FlyoutPlacement so Auto is never written
                // (WinUI's FlyoutBase validator rejects it — see FlyoutPlacement).
                FlyoutPlacement.Apply(menuFlyout, mf.Placement);
                foreach (var item in mf.Items) menuFlyout.Items.Add(MenuCommandFactory.CreateMenuFlyoutItem(item, this));
                return menuFlyout;
            }
            default:
            {
                var content = Mount(flyoutEl, requestRerender);
                return content is not null ? new WinUI.Flyout { Content = content } : null;
            }
        }
    }

    /// <summary>
    /// Spec 047 §14 Phase 3-final — descriptor-facing sibling of
    /// <see cref="CreateFlyoutFromElement"/>. Same shape as
    /// <see cref="IconResolver.ResolveIconForDescriptor"/>: a thin forwarder that tolerates
    /// <see langword="null"/> input so a <c>.OneWayBridged</c> entry on a
    /// button-family descriptor can wire
    /// <c>(c, v, rec, rr) =&gt; c.Flyout = rec.CreateFlyoutForDescriptor(v, rr)</c>
    /// without an upstream null guard.
    /// </summary>
    internal WinPrim.FlyoutBase? CreateFlyoutForDescriptor(Element? flyoutEl, Action requestRerender)
        => flyoutEl is null ? null : CreateFlyoutFromElement(flyoutEl, requestRerender);
}
