using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 9) — descriptor variant of the hand-coded
/// <c>MountSplitView</c> / <c>UpdateSplitView</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item>Two named slots — <c>Pane</c> and <c>Content</c> — both Element
///   typed, dispatched through the
///   <see cref="NamedSlots{TElement,TControl}"/> children strategy with
///   <c>GetCurrentChild</c> set on both slots for structural
///   reconciliation.</item>
///   <item><c>IsPaneOpen</c>, <c>OpenPaneLength</c>,
///   <c>CompactPaneLength</c>, <c>DisplayMode</c>,
///   <c>LightDismissOverlayMode</c> — plain <c>.OneWay</c> writes. The
///   legacy arm does not echo-suppress <c>IsPaneOpen</c> (writes are
///   unconditional, and <c>OnPaneOpenChanged</c> fires from both user and
///   programmatic transitions). The descriptor matches with twin
///   <c>.HandCodedEvent</c> entries — one per direction — that dispatch
///   the same callback with the right bool.</item>
///   <item><c>PaneBackground</c> — <c>.OneWayConditional</c> guarded on
///   non-null (legacy gate).</item>
/// </list></para>
///
/// <para><b>Known parity points:</b> the legacy arm gates the
/// PaneBackground update on <c>!ReferenceEquals(o.PaneBackground, n.PaneBackground)</c>;
/// the descriptor's reference-equality comparer reproduces that gate.</para>
/// </summary>
internal static class SplitViewDescriptor
{
    private static readonly NamedSlots<SplitViewElement, WinUI.SplitView> ChildrenStrategy =
        new NamedSlots<SplitViewElement, WinUI.SplitView>(new[]
        {
            new NamedSlot<SplitViewElement, WinUI.SplitView>(
                Name: "Pane",
                GetChild: static e => e.Pane,
                SetChild: static (c, ui) => c.Pane = ui)
            {
                GetCurrentChild = static c => c.Pane as UIElement,
            },
            new NamedSlot<SplitViewElement, WinUI.SplitView>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui as UIElement)
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
        });

    // Static trampolines — read the live element via Reconciler.GetElementTag
    // on every fire so a Tag refresh swaps the dispatch target without
    // re-subscription. Legacy parity: no ShouldSuppress gate — IsPaneOpen
    // writes are unconditional and the callback fires on every transition.
    private static readonly TypedEventHandler<WinUI.SplitView, object>
        PaneOpeningTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as SplitViewElement)
                ?.OnPaneOpenChanged?.Invoke(true);

    private static readonly TypedEventHandler<WinUI.SplitView, WinUI.SplitViewPaneClosingEventArgs>
        PaneClosingTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as SplitViewElement)
                ?.OnPaneOpenChanged?.Invoke(false);

    public static readonly ControlDescriptor<SplitViewElement, WinUI.SplitView> Descriptor =
        new ControlDescriptor<SplitViewElement, WinUI.SplitView>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.IsPaneOpen,
            set: static (c, v) => c.IsPaneOpen = v)
        .OneWay(
            get: static e => e.OpenPaneLength,
            set: static (c, v) => c.OpenPaneLength = v)
        .OneWay(
            get: static e => e.CompactPaneLength,
            set: static (c, v) => c.CompactPaneLength = v)
        .OneWay(
            get: static e => e.DisplayMode,
            set: static (c, v) => c.DisplayMode = v)
        .OneWay(
            get: static e => e.LightDismissOverlayMode,
            set: static (c, v) => c.LightDismissOverlayMode = v)
        .OneWayConditional(
            get:         static e => e.PaneBackground,
            set:         static (c, v) => c.PaneBackground = v!,
            shouldWrite: static e => e.PaneBackground is not null,
            comparer:    BrushReferenceComparer.Instance)
        .HandCodedEvent<SplitViewEventPayload,
            TypedEventHandler<WinUI.SplitView, object>>(
            subscribe:        static (c, h) => c.PaneOpening += h,
            callbackPresent:  static e => e.OnPaneOpenChanged,
            trampoline:       PaneOpeningTrampoline,
            slotIsNull:       static p => p.PaneOpeningTrampoline is null,
            setSlot:          static (p, h) => p.PaneOpeningTrampoline = h)
        .HandCodedEvent<SplitViewEventPayload,
            TypedEventHandler<WinUI.SplitView, WinUI.SplitViewPaneClosingEventArgs>>(
            subscribe:        static (c, h) => c.PaneClosing += h,
            callbackPresent:  static e => e.OnPaneOpenChanged,
            trampoline:       PaneClosingTrampoline,
            slotIsNull:       static p => p.PaneClosingTrampoline is null,
            setSlot:          static (p, h) => p.PaneClosingTrampoline = h);

    // Brush is a reference type (Microsoft.UI.Xaml.Media.Brush : DependencyObject);
    // ReferenceEquals on Brush?/Brush? matches the legacy arm's
    // !ReferenceEquals(o.PaneBackground, n.PaneBackground) gate exactly.
    private sealed class BrushReferenceComparer : IEqualityComparer<Brush?>
    {
        public static readonly BrushReferenceComparer Instance = new();
        public bool Equals(Brush? x, Brush? y) => ReferenceEquals(x, y);
        public int GetHashCode(Brush obj)
            => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
