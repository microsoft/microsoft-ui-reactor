using System.Diagnostics.CodeAnalysis;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountSplitButton</c> / <c>UpdateSplitButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> <c>Label</c> one-way (Content), <c>Click</c>
/// via <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
/// using a <c>TypedEventHandler&lt;SplitButton, SplitButtonClickEventArgs&gt;</c>
/// (the WinUI SplitButton.Click event signature).</para>
///
/// <para><b>Known gap vs. hand-coded handler:</b> <c>Flyout</c> is
/// escape-hatched — it requires the engine-internal
/// <c>CreateFlyoutFromElement</c> helper which the descriptor builders
/// don't yet expose. Authors needing a Flyout fall through to the
/// legacy arm (V1 OFF) or use setters chain.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class SplitButtonDescriptor
{
    private static readonly TypedEventHandler<WinUI.SplitButton, WinUI.SplitButtonClickEventArgs> ClickTrampoline =
        (s, _) => (Reconciler.GetElementTag(s) as SplitButtonElement)?.OnClick?.Invoke();

    public static readonly ControlDescriptor<SplitButtonElement, WinUI.SplitButton> Descriptor =
        new ControlDescriptor<SplitButtonElement, WinUI.SplitButton>
        {
            Children = new None<SplitButtonElement, WinUI.SplitButton>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v)
        .HandCodedEvent<SplitButtonEventPayload, TypedEventHandler<WinUI.SplitButton, WinUI.SplitButtonClickEventArgs>>(
            subscribe:        static (c, h) => c.Click += h,
            callbackPresent:  static e => e.OnClick,
            trampoline:       ClickTrampoline,
            slotIsNull:       static p => p.ClickTrampoline is null,
            setSlot:          static (p, h) => p.ClickTrampoline = h);
}
