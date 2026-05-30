using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 — registered descriptor for <see cref="ButtonElement"/>.
/// Supersedes the hand-coded <c>MountButton</c> / <c>UpdateButton</c> arms
/// (now removed); this is the live engine path.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Content</c> — polymorphic slot. When <c>ContentElement</c> is
///   set, the <see cref="SingleContent{TElement,TControl}"/> child strategy
///   mounts the child Element and assigns it to <c>Button.Content</c> (with
///   <c>GetCurrentChild</c> so re-renders structurally reconcile and preserve
///   descendant component state). When <c>ContentElement</c> is null, the
///   sibling <c>Label</c> <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional{TValue}"/>
///   (gated on <c>ContentElement is null</c>) writes the string label. The
///   guarded <c>SetChild</c> never nulls <c>Content</c>, so the string label
///   written in the prop loop survives the child-slot dispatch — the canonical
///   "element-or-string content" shape documented on
///   <see cref="ControlDescriptor{TElement,TControl}.ImperativeBridged"/>.</item>
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
            // Polymorphic Content: element child via SingleContent, string
            // Label via the gated OneWayConditional below. The guarded SetChild
            // only writes a non-null mounted child, so a Label string written
            // by the prop loop is never clobbered when ContentElement is null.
            Children = new SingleContent<ButtonElement, WinUI.Button>(
                GetChild: static e => e.ContentElement,
                SetChild: static (c, ui) => { if (ui is not null) c.Content = ui; })
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Label,
            set:         static (c, v) => c.Content = v,
            shouldWrite: static e => e.ContentElement is null)
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
