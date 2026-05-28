using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (9). Descriptor variant of the
/// hand-coded <c>MountFlipView</c> / <c>UpdateFlipView</c> arms for the
/// simple <see cref="FlipViewElement"/>. The typed
/// <c>TemplatedFlipViewElement&lt;T&gt;</c> peer is handled by
/// <see cref="TemplatedFlipViewDescriptor"/> (Phase 3 completion's
/// engine-gap closer, registered base-derived against
/// <c>TemplatedFlipViewElementBase</c>).
///
/// <para><b>Children:</b> reuses the existing
/// <see cref="ItemsHost{TElement,TControl}"/> strategy — the engine
/// pre-mounts each <c>Element</c> item and adds the mounted UIElement
/// to <c>FlipView.Items</c>. No new <c>PreMountedItems</c> strategy
/// needed; ItemsHost's flat <c>IList&lt;object&gt;</c> sink shape
/// already covers FlipView.</para>
///
/// <para><b>Coverage:</b> <c>SelectedIndex</c> +
/// <c>OnSelectedIndexChanged</c> via <c>.HandCodedControlled</c>; the
/// existing <see cref="ChangeEchoSuppressor"/> drains the programmatic
/// write echo.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class FlipViewDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var f = (WinUI.FlipView)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(f)) return;
        (Reconciler.GetElementTag(f) as FlipViewElement)?.OnSelectedIndexChanged?.Invoke(f.SelectedIndex);
    };

    public static readonly ControlDescriptor<FlipViewElement, WinUI.FlipView> Descriptor =
        new ControlDescriptor<FlipViewElement, WinUI.FlipView>
        {
            Children = new ItemsHost<FlipViewElement, WinUI.FlipView>(
                // Element[] -> IReadOnlyList<object> via array reference
                // covariance — same pattern ListBoxDescriptor uses for
                // string[] -> IReadOnlyList<object>. Engine pre-mounts each
                // Element item via ItemsHost's existing dispatch body.
                GetItems:      static e => (IReadOnlyList<object>)e.Items,
                GetCollection: static c => c.Items),
            GetSetters = static e => e.Setters,
        }
        .HandCodedControlled<FlipViewEventPayload, int,
            WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h);
}
