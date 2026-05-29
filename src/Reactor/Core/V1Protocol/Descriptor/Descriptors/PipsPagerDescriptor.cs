using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 11) — descriptor variant of the hand-coded
/// <c>MountPipsPager</c> / <c>UpdatePipsPager</c> arms in
/// <see cref="Reconciler"/>. Single-event round-trip on
/// <c>SelectedPageIndex</c>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>SelectedPageIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a typed <c>SelectedIndexChanged</c> trampoline gated on
///   <see cref="ChangeEchoSuppressor"/>. Mirrors the legacy guard's
///   programmatic-write semantics.</item>
///   <item><c>NumberOfPages</c>, <c>WrapMode</c>, <c>MaxVisiblePips</c>,
///   <c>PreviousButtonVisibility</c>, <c>NextButtonVisibility</c> — one-way
///   per legacy.</item>
/// </list></para>
/// </summary>
internal static class PipsPagerDescriptor
{
    private static readonly TypedEventHandler<WinUI.PipsPager, WinUI.PipsPagerSelectedIndexChangedEventArgs>
        SelectedIndexChangedTrampoline = (s, _) =>
        {
            var p = (WinUI.PipsPager)s!;
            if (ChangeEchoSuppressor.ShouldSuppressEcho(p, p.SelectedPageIndex)) return;
            (Reconciler.GetElementTag(p) as PipsPagerElement)
                ?.OnSelectedPageIndexChanged?.Invoke(p.SelectedPageIndex);
        };

    public static readonly ControlDescriptor<PipsPagerElement, WinUI.PipsPager> Descriptor =
        new ControlDescriptor<PipsPagerElement, WinUI.PipsPager>
        {
            Children = new None<PipsPagerElement, WinUI.PipsPager>(),
            GetSetters = static e => e.Setters,
        }
        // NumberOfPages BEFORE SelectedPageIndex so the index lands against
        // the widened range (WinUI clamps SelectedPageIndex against
        // NumberOfPages).
        .OneWay(
            get: static e => e.NumberOfPages,
            set: static (c, v) => c.NumberOfPages = v)
        .HandCodedControlled<PipsPagerEventPayload, int,
            TypedEventHandler<WinUI.PipsPager, WinUI.PipsPagerSelectedIndexChangedEventArgs>>(
            get:         static e => e.SelectedPageIndex,
            set:         static (c, v) => c.SelectedPageIndex = v,
            readBack:    static c => c.SelectedPageIndex,
            subscribe:   static (c, h) => c.SelectedIndexChanged += h,
            callback:    static e => e.OnSelectedPageIndexChanged,
            trampoline:  SelectedIndexChangedTrampoline,
            slotIsNull:  static p => p.SelectedIndexChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectedIndexChangedTrampoline = h,
            valueDiffEcho: true)
        .OneWay(
            get: static e => e.WrapMode,
            set: static (c, v) => c.WrapMode = v)
        .OneWay(
            get: static e => e.MaxVisiblePips,
            set: static (c, v) => c.MaxVisiblePips = v)
        .OneWay(
            get: static e => e.PreviousButtonVisibility,
            set: static (c, v) => c.PreviousButtonVisibility = v)
        .OneWay(
            get: static e => e.NextButtonVisibility,
            set: static (c, v) => c.NextButtonVisibility = v);
}
