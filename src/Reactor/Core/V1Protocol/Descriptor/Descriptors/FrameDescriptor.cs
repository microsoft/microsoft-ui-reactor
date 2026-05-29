using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Navigation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3-final Batch B — descriptor variant of the hand-coded
/// <c>MountFrame</c> / <c>UpdateFrame</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>SourcePageType</c> + <c>NavigationParameter</c> — modeled as a
///   single <see cref="ControlDescriptor{TElement,TControl}.Initial{TValue}"/>
///   entry against the element. The set lambda calls <c>Frame.Navigate</c>
///   ONCE on Mount; subsequent Update passes do not re-navigate (matches the
///   legacy <c>UpdateFrame</c> arm, which is a no-op beyond refreshing the
///   element tag). Treating this as <c>.OneWay</c> would re-navigate on every
///   element record-with, which is the wrong shape — Frame's navigation is
///   inherently imperative.</item>
///   <item><c>Navigated</c> / <c>Navigating</c> / <c>NavigationFailed</c> —
///   three <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   entries sharing a <see cref="FrameEventPayload"/> with one trampoline
///   slot per event. Each trampoline reads the live element via
///   <see cref="Reconciler.GetElementTag"/> and fires the corresponding
///   callback only when the element still has one wired — mirrors the
///   legacy arm's "always subscribe, read latest callback per fire"
///   pattern.</item>
/// </list></para>
///
/// <para><b>Known gap vs. hand-coded handler:</b> the descriptor gates each
/// of the three event subscriptions on the callback being present at Mount
/// time (standard <c>.HandCodedEvent</c> contract). The legacy
/// <c>MountFrame</c> subscribes unconditionally so a later record-with that
/// attaches a previously-null callback fires through the same trampoline
/// without re-wiring. The descriptor matches the established §14
/// "EnsureXxxWiring null-to-non-null" contract — fixes for later Update
/// transitions are handled by the entry on the next Mount/pool-rent. The
/// callback-on-mount common case is fully covered.</para>
/// </summary>
internal static class FrameDescriptor
{
    // ── Trampolines ──────────────────────────────────────────────────
    // Captured-free. Each fires the live element's callback (if still
    // present) via Reconciler.GetElementTag.

    private static readonly NavigatedEventHandler NavigatedTrampoline = (s, e) =>
    {
        if (Reconciler.GetElementTag((WinUI.Frame)s!) is FrameElement el && el.OnNavigated is { } h)
            h(e.SourcePageType);
    };

    private static readonly NavigatingCancelEventHandler NavigatingTrampoline = (s, e) =>
    {
        if (Reconciler.GetElementTag((WinUI.Frame)s!) is FrameElement el && el.OnNavigating is { } h)
            h(e.SourcePageType);
    };

    private static readonly NavigationFailedEventHandler NavigationFailedTrampoline = (s, e) =>
    {
        if (Reconciler.GetElementTag((WinUI.Frame)s!) is FrameElement el && el.OnNavigationFailed is { } h)
            h(e.SourcePageType, e.Exception);
    };

    public static readonly ControlDescriptor<FrameElement, WinUI.Frame> Descriptor =
        new ControlDescriptor<FrameElement, WinUI.Frame>
        {
            Children = new None<FrameElement, WinUI.Frame>(),
            GetSetters = static e => e.Setters,
        }
        // Mount-only navigation. Re-running this on Update would re-navigate
        // on every element record-with — Frame's navigation is inherently
        // imperative, so Update is a no-op (mirrors UpdateFrame). A single
        // .Initial entry projects both SourcePageType + NavigationParameter
        // via a value tuple so the set lambda has both pieces.
        .Initial<(global::System.Type? pageType, object? param)>(
            get: static e => (e.SourcePageType, e.NavigationParameter),
            set: static (c, v) =>
            {
                if (v.pageType is not null) c.Navigate(v.pageType, v.param);
            })
        .HandCodedEvent<FrameEventPayload, NavigatedEventHandler>(
            subscribe:        static (c, h) => c.Navigated += h,
            callbackPresent:  static e => e.OnNavigated,
            trampoline:       NavigatedTrampoline,
            slotIsNull:       static p => p.NavigatedTrampoline is null,
            setSlot:          static (p, h) => p.NavigatedTrampoline = h)
        .HandCodedEvent<FrameEventPayload, NavigatingCancelEventHandler>(
            subscribe:        static (c, h) => c.Navigating += h,
            callbackPresent:  static e => e.OnNavigating,
            trampoline:       NavigatingTrampoline,
            slotIsNull:       static p => p.NavigatingTrampoline is null,
            setSlot:          static (p, h) => p.NavigatingTrampoline = h)
        .HandCodedEvent<FrameEventPayload, NavigationFailedEventHandler>(
            subscribe:        static (c, h) => c.NavigationFailed += h,
            callbackPresent:  static e => e.OnNavigationFailed,
            trampoline:       NavigationFailedTrampoline,
            slotIsNull:       static p => p.NavigationFailedTrampoline is null,
            setSlot:          static (p, h) => p.NavigationFailedTrampoline = h);
}
