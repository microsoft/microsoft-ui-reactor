using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 9) — descriptor variant of the hand-coded
/// <c>MountTeachingTip</c> / <c>UpdateTeachingTip</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item>Two named slots — <c>Content</c> and <c>HeroContent</c> — both
///   Element typed, dispatched through the
///   <see cref="NamedSlots{TElement,TControl}"/> children strategy with
///   <c>GetCurrentChild</c> set for structural reconciliation.</item>
///   <item><c>Title</c>, <c>Subtitle</c>, <c>IsOpen</c>,
///   <c>PreferredPlacement</c>, <c>ActionButtonContent</c>,
///   <c>CloseButtonContent</c>, <c>PlacementMargin</c> — plain
///   <c>.OneWay</c> / <c>.OneWayConditional</c> writes.</item>
///   <item><c>IconSource</c> — <c>.OneWayConditional</c> with reference
///   comparer (mirrors legacy <c>!ReferenceEquals</c> gate). Resolved via
///   <see cref="Reconciler.ResolveIconSource(IconData?)"/>.</item>
///   <item><c>OnActionButtonClick</c>, <c>OnClosed</c> — fire-only
///   <c>.HandCodedEvent</c>s. ActionButtonClick fires when the user taps
///   the in-tip action button (which only exists when
///   <c>ActionButtonContent</c> is set); Closed fires when the tip is
///   dismissed.</item>
/// </list></para>
///
/// <para><b>Known gaps:</b>
/// <list type="bullet">
///   <item><b><c>Target</c> / <c>PlacementTarget</c> is escape-hatched</b>.
///   TeachingTip's <c>Target</c> property is a
///   <see cref="FrameworkElement"/> reference — it points at the control
///   the tip is anchored to, which is a sibling in the visual tree, not a
///   child the tip mounts. The descriptor framework can't express
///   "reference another element's mounted control"; legacy authors set
///   <c>Target</c> directly via a <c>.Set</c> imperative setter. The
///   descriptor follows the same pattern (setter escape).</item>
///   <item>The legacy arm re-mounts <c>HeroContent</c> wholesale on swap
///   (no structural reconcile); the descriptor uses the standard
///   NamedSlot reconciliation path, which preserves descendant state
///   across re-renders — strictly an improvement, but a documented
///   behavior difference.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class TeachingTipDescriptor
{
    private static readonly NamedSlots<TeachingTipElement, WinUI.TeachingTip> ChildrenStrategy =
        new NamedSlots<TeachingTipElement, WinUI.TeachingTip>(new[]
        {
            new NamedSlot<TeachingTipElement, WinUI.TeachingTip>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
            new NamedSlot<TeachingTipElement, WinUI.TeachingTip>(
                Name: "HeroContent",
                GetChild: static e => e.HeroContent,
                SetChild: static (c, ui) => c.HeroContent = ui)
            {
                GetCurrentChild = static c => c.HeroContent as UIElement,
            },
        });

    private static readonly TypedEventHandler<WinUI.TeachingTip, object>
        ActionButtonClickTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as TeachingTipElement)
                ?.OnActionButtonClick?.Invoke();

    private static readonly TypedEventHandler<WinUI.TeachingTip, WinUI.TeachingTipClosedEventArgs>
        ClosedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as TeachingTipElement)
                ?.OnClosed?.Invoke();

    public static readonly ControlDescriptor<TeachingTipElement, WinUI.TeachingTip> Descriptor =
        new ControlDescriptor<TeachingTipElement, WinUI.TeachingTip>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Title,
            set: static (c, v) => c.Title = v)
        .OneWay(
            get: static e => e.Subtitle ?? string.Empty,
            set: static (c, v) => c.Subtitle = v)
        .OneWay(
            get: static e => e.IsOpen,
            set: static (c, v) => c.IsOpen = v)
        .OneWay(
            get: static e => e.PlacementMargin,
            set: static (c, v) => c.PlacementMargin = v)
        .OneWay(
            get: static e => e.PreferredPlacement,
            set: static (c, v) => c.PreferredPlacement = v)
        .OneWayConditional(
            get:         static e => e.ActionButtonContent,
            set:         static (c, v) => c.ActionButtonContent = v!,
            shouldWrite: static e => e.ActionButtonContent is not null)
        .OneWayConditional(
            get:         static e => e.CloseButtonContent,
            set:         static (c, v) => c.CloseButtonContent = v!,
            shouldWrite: static e => e.CloseButtonContent is not null)
        .OneWayConditional(
            get:         static e => e.IconSource,
            set:         static (c, v) => c.IconSource = Reconciler.ResolveIconSource(v),
            shouldWrite: static e => e.IconSource is not null,
            comparer:    IconDataReferenceComparer.Instance)
        .HandCodedEvent<TeachingTipEventPayload,
            TypedEventHandler<WinUI.TeachingTip, object>>(
            subscribe:        static (c, h) => c.ActionButtonClick += h,
            callbackPresent:  static e => e.OnActionButtonClick,
            trampoline:       ActionButtonClickTrampoline,
            slotIsNull:       static p => p.ActionButtonClickTrampoline is null,
            setSlot:          static (p, h) => p.ActionButtonClickTrampoline = h)
        .HandCodedEvent<TeachingTipEventPayload,
            TypedEventHandler<WinUI.TeachingTip, WinUI.TeachingTipClosedEventArgs>>(
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
