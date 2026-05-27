using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 9) — descriptor variant of the hand-coded
/// <c>MountInfoBar</c> / <c>UpdateInfoBar</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Content</c> — single named slot dispatched through the
///   <see cref="NamedSlots{TElement,TControl}"/> children strategy with
///   <c>GetCurrentChild</c> set for structural reconciliation.</item>
///   <item><c>Title</c>, <c>Message</c>, <c>Severity</c>, <c>IsOpen</c>,
///   <c>IsClosable</c> — plain <c>.OneWay</c> writes. The legacy arm
///   doesn't echo-suppress <c>IsOpen</c> (no Opened event to round-trip
///   against; <c>Closed</c> is a one-shot dismissal callback) so the
///   descriptor matches with a OneWay + <c>.HandCodedEvent</c> on
///   <c>Closed</c>.</item>
///   <item><c>IconSource</c> — <c>.OneWayConditional</c> with reference
///   comparer (mirrors legacy <c>!ReferenceEquals</c> gate). Resolved via
///   <see cref="Reconciler.ResolveIconSource(IconData?)"/>.</item>
/// </list></para>
///
/// <para><b>Known gaps:</b>
/// <list type="bullet">
///   <item><b>ActionButton + <c>OnActionButtonClick</c> is escape-hatched</b>.
///   The legacy arm constructs an inner <c>Button</c> dynamically when
///   <c>ActionButtonContent</c> is non-null, then wires <c>Click</c> on
///   that dynamically-created child. The descriptor framework binds events
///   to the primary control, not a sub-control created during mount, so
///   this asymmetric pattern doesn't fit. Authors who need the action
///   button stay on V1 OFF (legacy arm), or use a <c>.Set</c> imperative
///   setter to construct the button themselves.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class InfoBarDescriptor
{
    private static readonly NamedSlots<InfoBarElement, WinUI.InfoBar> ChildrenStrategy =
        new NamedSlots<InfoBarElement, WinUI.InfoBar>(new[]
        {
            new NamedSlot<InfoBarElement, WinUI.InfoBar>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
        });

    private static readonly TypedEventHandler<WinUI.InfoBar, WinUI.InfoBarClosedEventArgs>
        ClosedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as InfoBarElement)?.OnClosed?.Invoke();

    public static readonly ControlDescriptor<InfoBarElement, WinUI.InfoBar> Descriptor =
        new ControlDescriptor<InfoBarElement, WinUI.InfoBar>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Title ?? string.Empty,
            set: static (c, v) => c.Title = v)
        .OneWay(
            get: static e => e.Message ?? string.Empty,
            set: static (c, v) => c.Message = v)
        .OneWay(
            get: static e => e.Severity,
            set: static (c, v) => c.Severity = v)
        .OneWay(
            get: static e => e.IsOpen,
            set: static (c, v) => c.IsOpen = v)
        .OneWay(
            get: static e => e.IsClosable,
            set: static (c, v) => c.IsClosable = v)
        .OneWayConditional(
            get:         static e => e.IconSource,
            set:         static (c, v) => c.IconSource = Reconciler.ResolveIconSource(v),
            shouldWrite: static e => e.IconSource is not null,
            comparer:    IconDataReferenceComparer.Instance)
        .HandCodedEvent<InfoBarEventPayload,
            TypedEventHandler<WinUI.InfoBar, WinUI.InfoBarClosedEventArgs>>(
            subscribe:        static (c, h) => c.Closed += h,
            callbackPresent:  static e => e.OnClosed,
            trampoline:       ClosedTrampoline,
            slotIsNull:       static p => p.ClosedTrampoline is null,
            setSlot:          static (p, h) => p.ClosedTrampoline = h);

    private sealed class IconDataReferenceComparer : IEqualityComparer<IconData?>
    {
        public static readonly IconDataReferenceComparer Instance = new();
        public bool Equals(IconData? x, IconData? y) => ReferenceEquals(x, y);
        public int GetHashCode(IconData obj)
            => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
