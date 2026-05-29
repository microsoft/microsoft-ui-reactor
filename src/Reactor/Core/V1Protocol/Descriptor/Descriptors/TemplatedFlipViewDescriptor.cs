using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of the hand-coded
/// <c>MountTemplatedFlipView</c> / <c>UpdateTemplatedFlipView</c> arms.
/// Closes the close-out carve documented in §14 Phase 3 finish.
///
/// <para>Registers against the non-generic intermediate base
/// <see cref="TemplatedFlipViewElementBase"/>, so every closed-T variant
/// (<see cref="TemplatedFlipViewElement{T}"/>) routes through this one
/// descriptor via the v1 registry's base-derived fallback walk.</para>
///
/// <para><b>Children strategy:</b>
/// <see cref="PreMountedItems{TElement,TControl}"/> — items come through
/// the element's <see cref="IItemViewSource"/> implementation
/// (<c>ItemCount</c> + <c>BuildItemView(int)</c>, inherited via
/// <see cref="TemplatedListElementBase"/>) and are pre-mounted up-front
/// into <c>FlipView.Items</c>. FlipView does not raise
/// <c>ContainerContentChanging</c> so it can't share the
/// <see cref="TemplatedItemsErased{TElement,TControl}"/> pipeline used
/// by ListView / GridView. Update path positionally reconciles each
/// slot via <see cref="Reconciler.ReconcileV1Child"/> — descendant
/// component state survives the per-slot Update branch when CanUpdate
/// matches, matching the legacy <c>UpdateTemplatedFlipView</c> body.</para>
///
/// <para><b>SelectedIndex:</b> bridged via <c>.HandCodedControlled</c>
/// with the shared <see cref="FlipViewEventPayload"/> slot. The
/// callback probe returns the base-virtual <c>InvokeSelectionChanged</c>
/// (gated on <c>HasCallbacks</c>) so the engine subscribes the
/// trampoline exactly when the closed-T leaf has a callback. The
/// trampoline re-reads the live element via
/// <see cref="Reconciler.GetElementTag"/> and dispatches through the
/// base virtual — the closed-T leaf invokes its own <c>OnSelectedIndexChanged</c>.
/// <see cref="ChangeEchoSuppressor"/> drains the programmatic-write
/// echo, same as the simple <see cref="FlipViewDescriptor"/>.</para>
///
/// <para><b>SelectedIndex initial write ordering:</b> runs AFTER the
/// binder (DescriptorHandler.Mount inlines the items binder before the
/// prop loop for <see cref="IItemsBinderStrategy"/> strategies) — by
/// the time the SelectedIndex prop setter fires, <c>FlipView.Items</c>
/// is populated and WinUI accepts the index.</para>
/// </summary>
internal static class TemplatedFlipViewDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var f = (WinUI.FlipView)s!;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(f, f.SelectedIndex)) return;
        (Reconciler.GetElementTag(f) as TemplatedFlipViewElementBase)?.InvokeSelectionChanged(f.SelectedIndex);
    };

    public static readonly ControlDescriptor<TemplatedFlipViewElementBase, WinUI.FlipView> Descriptor =
        new ControlDescriptor<TemplatedFlipViewElementBase, WinUI.FlipView>
        {
            Children = new PreMountedItems<TemplatedFlipViewElementBase, WinUI.FlipView>(
                GetSource:     static el => (IItemViewSource)el,
                GetCollection: static c => c.Items),
            GetSetters = static el => el.HasSetters
                ? new global::System.Action<WinUI.FlipView>[] { ctrl => el.ApplyControlSetters(ctrl) }
                : global::System.Array.Empty<global::System.Action<WinUI.FlipView>>(),
        }
        .HandCodedControlled<FlipViewEventPayload, int,
            WinUI.SelectionChangedEventHandler>(
            get:         static el => el.GetSelectedIndex(),
            set:         static (ctrl, v) => { if (v >= 0) ctrl.SelectedIndex = v; },
            readBack:    static ctrl => ctrl.SelectedIndex,
            subscribe:   static (ctrl, h) => ctrl.SelectionChanged += h,
            // HasCallbacks gates whether the engine subscribes — return a
            // non-null synthetic delegate when the leaf has a callback so
            // the engine wires the trampoline. The trampoline ignores the
            // returned delegate; it re-reads the element via Tag and
            // invokes the base virtual InvokeSelectionChanged directly.
            callback:    static el => el.HasCallbacks
                            ? (global::System.Action<int>)el.InvokeSelectionChanged
                            : null,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
            valueDiffEcho: true);
}
