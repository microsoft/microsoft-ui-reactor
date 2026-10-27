using System.Diagnostics.CodeAnalysis;
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
/// <remarks>
/// Spec 058 §15 (P5.28) — migrated to a generated monomorphic decorator
/// (<c>[WrapDecorator]</c>): the generated Pattern-A cctor self-registers the
/// decorator on first <c>new</c>, replacing the hand-written
/// <c>XamlPageDescriptor</c>. The control is a <see cref="Frame"/> created once
/// and re-navigated in place on update.
/// </remarks>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Frame))]
[global::Microsoft.UI.Reactor.Wrappers.WrapDecorator(nameof(CreateFrame), OnUpdate = nameof(UpdateFrame), OnUnmount = nameof(TeardownFrame))]
public partial record XamlPageElement(
    Type PageType,
    object? Parameter = null) : Element
{
    private static Frame CreateFrame(XamlPageElement element)
    {
        var frame = new Frame();
        Navigate(frame, element);
        return frame;
    }

    private static void UpdateFrame(XamlPageElement oldEl, XamlPageElement newEl, Frame frame)
    {
        if (oldEl.PageType != newEl.PageType || !Equals(oldEl.Parameter, newEl.Parameter))
            Navigate(frame, newEl);
    }

    // Navigating to a page type the XAML metadata chain cannot resolve terminates the
    // process with an access violation inside native WinUI, so the guarded seam is
    // mandatory here too — XamlPage's whole purpose is hosting app-defined pages.
    // XamlPageElement has no navigation-failed callback, so a refusal throws: a managed
    // exception is catchable and diagnosable, an access violation is neither.
    internal static void Navigate(Frame frame, XamlPageElement element)
    {
        var failure = FrameNavigation.TryNavigate(frame, element.PageType, element.Parameter);
        if (failure is not null) throw failure;
    }

    // Navigate away (clear Content) to trigger Page.OnNavigatedFrom cleanup.
    private static void TeardownFrame(Frame frame) => frame.Content = null;
}

/// <summary>
/// An element that embeds an arbitrary FrameworkElement (UserControl, custom control, etc.)
/// into the Reactor tree. The factory creates the control; the updater patches it.
/// </summary>
/// <remarks>
/// Spec 058 §15 (P5.28) — migrated to a generated monomorphic decorator
/// (<c>[WrapDecorator]</c>), replacing the hand-written <c>XamlHostDescriptor</c>.
/// </remarks>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(FrameworkElement))]
[global::Microsoft.UI.Reactor.Wrappers.WrapDecorator(nameof(CreateHost), OnUpdate = nameof(UpdateHost))]
public partial record XamlHostElement(
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

    private static FrameworkElement CreateHost(XamlHostElement element)
    {
        var control = element.Factory();
        element.Updater?.Invoke(control);
        return control;
    }

    private static void UpdateHost(XamlHostElement oldEl, XamlHostElement newEl, FrameworkElement control)
        => newEl.Updater?.Invoke(control);
}

/// <summary>
/// Registers the reverse-embedding element types with a Reconciler.
/// Call this once during app startup or ReactorHostControl initialization.
///
/// Usage:
///   XamlInterop.Register(reconciler);
///
/// Then in a Reactor component (the factories record a call site for the source map; the
/// record constructors work too, without one):
///   XamlPage(typeof(ExistingXamlPage), "param")
///   XamlHost(() => new MyUserControl(), ctrl => ((MyUserControl)ctrl).Value = 42)
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
                XamlPageElement.Navigate(frame, el);
                Reconciler.SetElementTag(frame, el);
                return frame;
            },
            update: (r, oldEl, newEl, frame, rerender) =>
            {
                if (oldEl.PageType != newEl.PageType || !Equals(oldEl.Parameter, newEl.Parameter))
                    XamlPageElement.Navigate(frame, newEl);
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
