using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountSplitButton</c> / <c>UpdateSplitButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Label</c> — one-way (Content).</item>
///   <item><c>Click</c> via
///   <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   using a <c>TypedEventHandler&lt;SplitButton, SplitButtonClickEventArgs&gt;</c>
///   (the WinUI SplitButton.Click event signature).</item>
///   <item><c>Flyout</c> — <c>.OneWayBridged&lt;Element?&gt;</c> entry whose
///   set lambda calls <c>Reconciler.CreateFlyoutForDescriptor(v, rr)</c>
///   to produce a <c>FlyoutBase?</c> and assign it to
///   <c>SplitButton.Flyout</c>. Mirrors the legacy mount arm's flyout
///   construction path. Comparer is
///   <see cref="ElementReferenceComparer"/> (reference identity over
///   <c>Element?</c>) — matches the <c>GridDescriptor</c> definition-rebuild
///   pattern, so the flyout is only torn down + rebuilt when the
///   Flyout element reference actually changes.</item>
/// </list></para>
/// </summary>
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
            setSlot:          static (p, h) => p.ClickTrampoline = h)
        .OneWayBridged<Element?>(
            get:         static e => e.Flyout,
            set:         static (c, v, rec, rr) => c.Flyout = rec.CreateFlyoutForDescriptor(v, rr),
            shouldWrite: static e => e.Flyout is not null,
            comparer:    ElementReferenceComparer.Instance);

}
