using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Hosting;

// ════════════════════════════════════════════════════════════════════════
//  Feature 7: Reverse Embedding — XAML pages and controls inside Reactor
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// An element that embeds a XAML Page inside a Frame, enabling navigation
/// to existing XAML pages from within a Reactor component tree.
/// </summary>
public record XamlPageElement(Type PageType, object? Parameter = null) : Element;

/// <summary>
/// An element that embeds an arbitrary FrameworkElement (UserControl, custom control, etc.)
/// into the Reactor tree. The factory creates the control; the updater patches it.
/// </summary>
public record XamlHostElement(
    Func<FrameworkElement> Factory,
    Action<FrameworkElement>? Updater = null
) : Element
{
    /// <summary>
    /// Optional discriminator for the reconciler's CanUpdate check.
    /// When set, two XamlHostElements can only update in place if their
    /// TypeKeys match. Use this to prevent unrelated host elements from
    /// being reconciled against each other.
    /// </summary>
    public string? TypeKey { get; init; }
}

/// <summary>
/// Registers the reverse-embedding element types with a Reconciler.
/// Call this once during app startup or ReactorHostControl initialization.
///
/// Usage:
///   XamlInterop.Register(reconciler);
///
/// Then in a Reactor component:
///   new XamlPageElement(typeof(ExistingXamlPage), "param")
///   new XamlHostElement(() => new MyUserControl(), ctrl => ((MyUserControl)ctrl).Value = 42)
/// </summary>
public static class XamlInterop
{
    public static void Register(Reconciler reconciler)
    {
        // Spec 047 §14 Phase 4 (4.0.5): V1 auto-registration owns these two
        // element types when the V1 protocol is active. Skip any type that is
        // already registered so this call stays idempotent and never trips the
        // EnsureRegistrableElementType duplicate-registration guard (whether the
        // prior registration came from V1 built-ins or an earlier Register call).
        // ── XamlPageElement → Frame ──────────────────────────────────
        if (!reconciler.IsElementTypeRegistered(typeof(XamlPageElement)))
        reconciler.RegisterType<XamlPageElement, Frame>(
            mount: (r, el, rerender) =>
            {
                var frame = new Frame();
                frame.Navigate(el.PageType, el.Parameter);
                Reconciler.SetElementTag(frame, el);
                return frame;
            },
            update: (r, oldEl, newEl, frame, rerender) =>
            {
                if (oldEl.PageType != newEl.PageType || !Equals(oldEl.Parameter, newEl.Parameter))
                    frame.Navigate(newEl.PageType, newEl.Parameter);
                Reconciler.SetElementTag(frame, newEl);
                return null; // updated in place
            },
            unmount: (r, frame) =>
            {
                // Navigate away to trigger Page.OnNavigatedFrom cleanup
                if (frame.Content is Page)
                    frame.Content = null;
            });

        // ── XamlHostElement → FrameworkElement ───────────────────────
        if (!reconciler.IsElementTypeRegistered(typeof(XamlHostElement)))
        reconciler.RegisterType<XamlHostElement, FrameworkElement>(
            mount: (r, el, rerender) =>
            {
                var control = el.Factory();
                el.Updater?.Invoke(control);
                Reconciler.SetElementTag(control, el);
                return control;
            },
            update: (r, oldEl, newEl, control, rerender) =>
            {
                newEl.Updater?.Invoke(control);
                Reconciler.SetElementTag(control, newEl);
                return null; // updated in place
            },
            unmount: (r, control) =>
            {
                // XamlHostElement content is created outside Reactor's tree.
                // Do NOT recurse into children — they were never managed by Reactor
                // and must not be pooled (they may have stale parent references
                // or be types Reactor doesn't know how to clean).
                //
                // Fully detach reactor state: the host control may outlive this
                // unmount (app may retain a reference and reuse it). Clearing
                // Current* handlers ensures stale reactor callbacks can't fire.
                Reconciler.DetachReactorState(control);
            });
    }
}
