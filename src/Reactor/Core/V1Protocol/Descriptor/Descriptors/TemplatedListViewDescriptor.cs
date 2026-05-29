using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 close-out — descriptor variant of the hand-coded
/// <c>MountTemplatedListView</c> / <c>UpdateTemplatedListView</c> arms.
///
/// <para>Registers against the non-generic intermediate base
/// <see cref="TemplatedListViewElementBase"/>, so every closed-T variant
/// (<see cref="TemplatedListViewElement{T}"/>) routes through this one
/// descriptor via the v1 registry's base-derived fallback walk. The
/// strategy is <see cref="TemplatedItemsErased{TElement,TControl}"/> —
/// items + keys are read through the element's
/// <see cref="IKeyedItemSource"/> implementation, so the descriptor
/// itself is non-generic in TItem.</para>
///
/// <para><b>Event wiring lives inside
/// <see cref="Reconciler.BindKeyedItemsSource"/></b> (SelectionChanged +
/// ItemClick subscribed once at Mount with trampolines that re-read the
/// live element via <see cref="Reconciler.GetElementTag"/>) — avoiding a
/// new <c>ControlEventState</c> payload box just for this descriptor.
/// Selection / Click semantics match the legacy
/// <c>MountTemplatedListView</c> body 1:1, including the
/// <see cref="ReactorRow"/>.Index translation under the OC delta path.</para>
/// </summary>
internal static class TemplatedListViewDescriptor
{
    public static readonly ControlDescriptor<TemplatedListViewElementBase, WinUI.ListView> Descriptor =
        new ControlDescriptor<TemplatedListViewElementBase, WinUI.ListView>
        {
            Children = new TemplatedItemsErased<TemplatedListViewElementBase, WinUI.ListView>(
                GetSource: static el => (IKeyedItemSource)el),
            GetSetters = static el => el.HasSetters
                ? new global::System.Action<WinUI.ListView>[] { ctrl => el.ApplyControlSetters(ctrl) }
                : global::System.Array.Empty<global::System.Action<WinUI.ListView>>(),
        }
        .OneWayConditional(
            get:         static el => el.GetSelectionMode(),
            set:         static (ctrl, v) => ctrl.SelectionMode = v,
            shouldWrite: static _ => true)
        .OneWayConditional(
            get:         static el => el.GetIsItemClickEnabled(),
            set:         static (ctrl, v) => ctrl.IsItemClickEnabled = v,
            shouldWrite: static _ => true)
        .OneWayConditional(
            get:         static el => el.GetHeader(),
            set:         static (ctrl, v) => { if (v is not null) ctrl.Header = v; },
            shouldWrite: static el => el.GetHeader() is not null)
        // SelectedIndex runs AFTER the binder (DescriptorHandler.Mount inlines
        // ItemsSource binding before the prop loop for templated-items
        // strategies — same ordering rationale as ItemsHost).
        .OneWayConditional(
            get:         static el => el.GetSelectedIndex(),
            set:         static (ctrl, v) => { if (v >= 0) ctrl.SelectedIndex = v; },
            shouldWrite: static el => el.GetSelectedIndex() >= 0);
}

/// <summary>
/// Spec 047 §14 Phase 3 close-out — descriptor variant of the hand-coded
/// <c>MountTemplatedGridView</c> / <c>UpdateTemplatedGridView</c> arms.
/// Mirror of <see cref="TemplatedListViewDescriptor"/> targeting
/// <see cref="WinUI.GridView"/>; same erased strategy + binder path.
/// </summary>
internal static class TemplatedGridViewDescriptor
{
    public static readonly ControlDescriptor<TemplatedGridViewElementBase, WinUI.GridView> Descriptor =
        new ControlDescriptor<TemplatedGridViewElementBase, WinUI.GridView>
        {
            Children = new TemplatedItemsErased<TemplatedGridViewElementBase, WinUI.GridView>(
                GetSource: static el => (IKeyedItemSource)el),
            GetSetters = static el => el.HasSetters
                ? new global::System.Action<WinUI.GridView>[] { ctrl => el.ApplyControlSetters(ctrl) }
                : global::System.Array.Empty<global::System.Action<WinUI.GridView>>(),
        }
        .OneWayConditional(
            get:         static el => el.GetSelectionMode(),
            set:         static (ctrl, v) => ctrl.SelectionMode = v,
            shouldWrite: static _ => true)
        .OneWayConditional(
            get:         static el => el.GetIsItemClickEnabled(),
            set:         static (ctrl, v) => ctrl.IsItemClickEnabled = v,
            shouldWrite: static _ => true)
        .OneWayConditional(
            get:         static el => el.GetHeader(),
            set:         static (ctrl, v) => { if (v is not null) ctrl.Header = v; },
            shouldWrite: static el => el.GetHeader() is not null)
        .OneWayConditional(
            get:         static el => el.GetSelectedIndex(),
            set:         static (ctrl, v) => { if (v >= 0) ctrl.SelectedIndex = v; },
            shouldWrite: static el => el.GetSelectedIndex() >= 0);
}
