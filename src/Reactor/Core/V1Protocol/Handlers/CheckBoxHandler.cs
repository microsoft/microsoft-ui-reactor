using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 — CheckBox value-control handler (V1-owned).
//
// CheckBox is a value control whose suppress-token bookkeeping (ChangeEchoSuppressor
// + SetElementTag-first) differs from the generic `.Controlled` write path used by
// CheckBoxDescriptor: the descriptor calls the suppressed setter whenever the
// element's old != new value, even when the control already holds the new value (a
// no-op same-value write). When WinUI raises no event for that no-op, the suppress
// token is stranded and swallows the user's *next* real toggle. This handler only
// begins suppression when cb.IsChecked != target, avoiding the stranded token, and
// fully supports three-state mode + OnCheckedStateChanged.
//
// CheckBox is a ContentControl (Label -> Content) with no Reactor child elements;
// ContinueDefaultTraversal keeps unmount/pool behavior identical to the engine's
// default recursion. The unregistered CheckBoxDescriptor is retained for isolated
// selftests.

/// <summary>§14 — CheckBox (value control; echo-suppression-aware).</summary>
internal sealed class CheckBoxHandler : IDecoratorElementHandler<CheckBoxElement>
{
    public UIElement Mount(MountContext ctx, CheckBoxElement el)
    {
        var checkBox = new WinUI.CheckBox { Content = el.Label };
        if (el.IsThreeState)
        {
            checkBox.IsThreeState = true;
            checkBox.IsChecked = el.CheckedState;
        }
        else
        {
            checkBox.IsChecked = el.IsChecked;
        }
        Reconciler.SetElementTag(checkBox, el);
        if (el.OnIsCheckedChanged is not null || el.OnCheckedStateChanged is not null)
        {
            checkBox.Checked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var cbe = Reconciler.GetElementTag(c) as CheckBoxElement;
                cbe?.OnIsCheckedChanged?.Invoke(true);
                cbe?.OnCheckedStateChanged?.Invoke(true);
            };
            checkBox.Unchecked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var cbe = Reconciler.GetElementTag(c) as CheckBoxElement;
                cbe?.OnIsCheckedChanged?.Invoke(false);
                cbe?.OnCheckedStateChanged?.Invoke(false);
            };
            checkBox.Indeterminate += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var cbe = Reconciler.GetElementTag(c) as CheckBoxElement;
                cbe?.OnCheckedStateChanged?.Invoke(null);
            };
        }
        Reconciler.ApplySetters(el.Setters, checkBox);
        return checkBox;
    }

    public UIElement Update(UpdateContext ctx, CheckBoxElement oldEl, CheckBoxElement newEl, UIElement control)
    {
        var cb = (WinUI.CheckBox)control;
        Reconciler.SetElementTag(cb, newEl);
        bool oldWired = oldEl.OnIsCheckedChanged is not null || oldEl.OnCheckedStateChanged is not null;
        bool newWired = newEl.OnIsCheckedChanged is not null || newEl.OnCheckedStateChanged is not null;
        if (!oldWired && newWired)
        {
            cb.Checked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var cbe = Reconciler.GetElementTag(c) as CheckBoxElement;
                cbe?.OnIsCheckedChanged?.Invoke(true);
                cbe?.OnCheckedStateChanged?.Invoke(true);
            };
            cb.Unchecked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var cbe = Reconciler.GetElementTag(c) as CheckBoxElement;
                cbe?.OnIsCheckedChanged?.Invoke(false);
                cbe?.OnCheckedStateChanged?.Invoke(false);
            };
            cb.Indeterminate += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var cbe = Reconciler.GetElementTag(c) as CheckBoxElement;
                cbe?.OnCheckedStateChanged?.Invoke(null);
            };
        }
        cb.Content = newEl.Label;
        cb.IsThreeState = newEl.IsThreeState;
        var target = newEl.IsThreeState ? newEl.CheckedState : newEl.IsChecked;
        if (cb.IsChecked != target)
        {
            ChangeEchoSuppressor.BeginSuppress(cb);
            cb.IsChecked = target;
        }
        Reconciler.ApplySetters(newEl.Setters, cb);
        return control;
    }

    public V1UnmountDisposition Unmount(UnmountContext ctx, CheckBoxElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
