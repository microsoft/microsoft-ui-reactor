using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountHyperlinkButton</c> / <c>UpdateHyperlinkButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Content</c> — one-way (string Content).</item>
///   <item><c>NavigateUri</c> — one-way (transitions to null clear the
///   stale navigation target — matches the legacy Update arm's
///   unconditional re-write).</item>
///   <item><c>Click</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   with <c>RoutedEventHandler</c> trampoline reading the live element
///   on each fire.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class HyperlinkButtonDescriptor
{
    private static readonly RoutedEventHandler ClickTrampoline = (s, _) =>
        (Reconciler.GetElementTag((WinUI.HyperlinkButton)s!) as HyperlinkButtonElement)?.OnClick?.Invoke();

    public static readonly ControlDescriptor<HyperlinkButtonElement, WinUI.HyperlinkButton> Descriptor =
        new ControlDescriptor<HyperlinkButtonElement, WinUI.HyperlinkButton>
        {
            Children = new None<HyperlinkButtonElement, WinUI.HyperlinkButton>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Content,
            set: static (c, v) => c.Content = v)
        .OneWay<Uri?>(
            get: static e => e.NavigateUri,
            set: static (c, v) =>
            {
                if (v is null) c.ClearValue(WinUI.HyperlinkButton.NavigateUriProperty);
                else c.NavigateUri = v;
            })
        .HandCodedEvent<HyperlinkButtonEventPayload, RoutedEventHandler>(
            subscribe:        static (c, h) => c.Click += h,
            callbackPresent:  static e => e.OnClick,
            trampoline:       ClickTrampoline,
            slotIsNull:       static p => p.ClickTrampoline is null,
            setSlot:          static (p, h) => p.ClickTrampoline = h);
}
