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
/// <para><b>ActionButton + <c>OnActionButtonClick</c> (Phase 3-final Batch F):</b>
/// the legacy arm constructs an inner <c>Button</c> dynamically when
/// <c>ActionButtonContent</c> is non-null, then wires <c>Click</c> on the
/// dynamically-created child. The descriptor models this as a
/// <c>.OneWayBridged&lt;string?&gt;</c> entry whose set lambda creates the
/// <c>Button</c>, wires <c>Click</c> via a closure over the parent InfoBar
/// reference (which is rooted by the InfoBar's Tag — Click resolves
/// <c>OnActionButtonClick</c> through <see cref="Reconciler.GetElementTag"/>
/// so a record-with that updates the callback picks up automatically). The
/// gate matches the legacy "set when non-null" treatment: a non-null →
/// non-null content swap rebuilds the inner button (legacy doesn't rebuild,
/// but the rebuild is observably the same when the click handler reads the
/// live element tag).</para>
/// </summary>
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
        // ActionButton (Phase 3-final Batch F). Dynamically construct the inner
        // Button; Click trampoline reads the live element via GetElementTag so a
        // later record-with that updates OnActionButtonClick picks up
        // automatically (Update keeps the InfoBar's Tag fresh).
        .OneWayBridged<string?>(
            get:         static e => e.ActionButtonContent,
            set:         static (c, v, _, _) =>
            {
                if (v is null)
                {
                    c.ActionButton = null;
                    return;
                }
                var btn = new WinUI.Button { Content = v };
                var infoBar = c;
                btn.Click += (_, _) =>
                    (Reconciler.GetElementTag(infoBar) as InfoBarElement)
                        ?.OnActionButtonClick?.Invoke();
                c.ActionButton = btn;
            },
            shouldWrite: static e => e.ActionButtonContent is not null)
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
