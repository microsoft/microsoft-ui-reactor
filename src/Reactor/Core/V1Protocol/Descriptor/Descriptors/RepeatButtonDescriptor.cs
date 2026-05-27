using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountRepeatButton</c> / <c>UpdateRepeatButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> <c>Label</c> (Content) / <c>Delay</c> /
/// <c>Interval</c> one-way, <c>Click</c> via
/// <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class RepeatButtonDescriptor
{
    private static readonly RoutedEventHandler ClickTrampoline = (s, _) =>
        (Reconciler.GetElementTag((WinPrim.RepeatButton)s!) as RepeatButtonElement)?.OnClick?.Invoke();

    public static readonly ControlDescriptor<RepeatButtonElement, WinPrim.RepeatButton> Descriptor =
        new ControlDescriptor<RepeatButtonElement, WinPrim.RepeatButton>
        {
            Children = new None<RepeatButtonElement, WinPrim.RepeatButton>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v)
        .OneWay(
            get: static e => e.Delay,
            set: static (c, v) => c.Delay = v)
        .OneWay(
            get: static e => e.Interval,
            set: static (c, v) => c.Interval = v)
        .HandCodedEvent<RepeatButtonEventPayload, RoutedEventHandler>(
            subscribe:        static (c, h) => c.Click += h,
            callbackPresent:  static e => e.OnClick,
            trampoline:       ClickTrampoline,
            slotIsNull:       static p => p.ClickTrampoline is null,
            setSlot:          static (p, h) => p.ClickTrampoline = h);
}
