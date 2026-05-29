using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountButton</c> / <c>UpdateButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Label</c> — one-way (Content).</item>
///   <item><c>IsEnabled</c> / <c>IsDisabledFocusable</c> — the legacy
///   "focusable-disabled" treatment: when <c>IsDisabledFocusable=true</c>
///   force <c>IsEnabled=true</c> and dim Opacity to 0.4 so Tab still reaches
///   the control; when false, <c>ClearValue(OpacityProperty)</c> so a XAML
///   style's Opacity setter still wins, and write <c>IsEnabled</c> normally.</item>
///   <item><c>Click</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   with <c>RoutedEventHandler</c> trampoline. The trampoline reads the
///   live element on each fire and short-circuits when
///   <c>IsDisabledFocusable</c> is true (mirrors the legacy guard).</item>
/// </list></para>
///
/// <para><b>Known gap vs. hand-coded handler — and why this descriptor is no
/// longer the registered engine path:</b> <c>ContentElement</c> (Button
/// hosting a child Element rather than a string label) is not expressed here —
/// the descriptor only handles the string-Content fast path. Because dropping
/// element content is a real regression once the type is registered (the
/// legacy fall-through is removed), the engine instead registers the delegate
/// <c>ButtonHandler</c>, which runs the COMPLETE legacy
/// <c>MountButton</c>/<c>UpdateButton</c> bodies (including
/// <c>ContentElement</c>). This descriptor is retained for its isolated
/// selftests and the perf-bench descriptor variant.</para>
/// </summary>
internal static class ButtonDescriptor
{
    private static readonly RoutedEventHandler ClickTrampoline = (s, _) =>
    {
        if (Reconciler.GetElementTag((WinUI.Button)s!) is ButtonElement live)
        {
            if (live.IsDisabledFocusable) return;
            live.OnClick?.Invoke();
        }
    };

    public static readonly ControlDescriptor<ButtonElement, WinUI.Button> Descriptor =
        new ControlDescriptor<ButtonElement, WinUI.Button>
        {
            Children = new None<ButtonElement, WinUI.Button>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v)
        // IsEnabled — written normally only when NOT in the focusable-
        // disabled dim mode. The OneWayConditional re-writes when the
        // predicate flips false→true (i.e. exiting dim mode) so authors
        // see their IsEnabled value restored after the override clears.
        .OneWayConditional(
            get:         static e => e.IsEnabled,
            set:         static (c, v) => c.IsEnabled = v,
            shouldWrite: static e => !e.IsDisabledFocusable)
        // IsDisabledFocusable transition — when true, force IsEnabled=true
        // and Opacity=0.4; when false, ClearValue(Opacity) so any style /
        // theme Opacity binding survives.
        .OneWay<bool>(
            get: static e => e.IsDisabledFocusable,
            set: static (c, v) =>
            {
                if (v)
                {
                    c.IsEnabled = true;
                    c.Opacity = 0.4;
                }
                else
                {
                    c.ClearValue(UIElement.OpacityProperty);
                }
            })
        .HandCodedEvent<ButtonEventPayload, RoutedEventHandler>(
            subscribe:        static (c, h) => c.Click += h,
            callbackPresent:  static e => e.OnClick,
            trampoline:       ClickTrampoline,
            slotIsNull:       static p => p.ClickTrampoline is null,
            setSlot:          static (p, h) => p.ClickTrampoline = h);
}
