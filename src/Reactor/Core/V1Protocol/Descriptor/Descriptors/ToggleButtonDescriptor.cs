using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountToggleButton</c> / <c>UpdateToggleButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Label</c> — one-way (Content).</item>
///   <item><c>IsThreeState</c> — one-way write before <c>IsChecked</c>
///   to satisfy the legacy ordering invariant (3-state mode flips before
///   the nullable IsChecked write).</item>
///   <item><c>IsChecked</c> — one-way; in 3-state mode the controlled
///   value source is <c>CheckedState</c> (bool?), otherwise <c>IsChecked</c>
///   (non-nullable bool widened to bool?).</item>
///   <item><c>Click</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   with a <c>RoutedEventHandler</c> trampoline that reads back the
///   control's <c>IsChecked</c> and fires BOTH <c>OnIsCheckedChanged(bool)</c>
///   AND <c>OnCheckedStateChanged(bool?)</c> — mirrors the legacy arm.
///   <c>Click</c> (not <c>Checked</c>/<c>Unchecked</c>) is the subscription
///   target so programmatic re-writes during Update don't echo through the
///   user callback.</item>
/// </list></para>
///
/// <para><b>Known gap vs. hand-coded handler:</b> the descriptor writes
/// the IsChecked / IsThreeState pair in declarative order but the
/// transition <c>IsThreeState=false→true</c> + <c>CheckedState=null</c>
/// in the same Update is not fully exercised by the fixture — WinUI's
/// ToggleButton template may sequence those writes through visual states
/// in ways the legacy imperative arm avoids by branching first. Authors
/// who need three-state mode at construction (Mount-time) should hit the
/// fast path; mid-stream transitions into three-state remain on the
/// legacy arm (V1 OFF).</para>
/// </summary>
internal static class ToggleButtonDescriptor
{
    private static readonly RoutedEventHandler ClickTrampoline = (s, _) =>
    {
        var t = (WinPrim.ToggleButton)s!;
        if (Reconciler.GetElementTag(t) is not ToggleButtonElement live) return;
        live.OnIsCheckedChanged?.Invoke(t.IsChecked ?? false);
        live.OnCheckedStateChanged?.Invoke(t.IsChecked);
    };

    public static readonly ControlDescriptor<ToggleButtonElement, WinPrim.ToggleButton> Descriptor =
        new ControlDescriptor<ToggleButtonElement, WinPrim.ToggleButton>
        {
            Children = new None<ToggleButtonElement, WinPrim.ToggleButton>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v)
        .OneWay(
            get: static e => e.IsThreeState,
            set: static (c, v) => c.IsThreeState = v)
        .OneWay<bool?>(
            get: static e => e.IsThreeState ? e.CheckedState : e.IsChecked,
            set: static (c, v) => c.IsChecked = v)
        .HandCodedEvent<ToggleButtonEventPayload, RoutedEventHandler>(
            subscribe:        static (c, h) => c.Click += h,
            callbackPresent:  static e => (Delegate?)e.OnIsCheckedChanged ?? e.OnCheckedStateChanged,
            trampoline:       ClickTrampoline,
            slotIsNull:       static p => p.ClickTrampoline is null,
            setSlot:          static (p, h) => p.ClickTrampoline = h);
}
