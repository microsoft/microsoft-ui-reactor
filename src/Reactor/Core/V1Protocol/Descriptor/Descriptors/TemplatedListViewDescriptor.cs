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
/// live element via <see cref="Reconciler.GetElementTag(Microsoft.UI.Xaml.UIElement)"/>) — avoiding a
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
        //
        // Issue #1090 — gate on reachability as well as sign. WinUI will not
        // honor a SelectedIndex past the end of its ItemsSource: the write
        // raises no SelectionChanged (so a suppression token armed for it would
        // strand and later eat a real user selection) and the setter throws
        // ArgumentException outright.
        //
        // Applied here for CONSISTENCY, not because a fixture reaches it: these
        // descriptor ports are "retained for isolated selftests" (see
        // TemplatedListHandler.cs) and are not registered on the production
        // path, which goes through the TemplatedListHandler decorator instead.
        // They carry the identical write shape, so guarding them keeps the
        // family from drifting if they are ever re-registered.
        .OneWayConditional(
            get:         static el => el.GetSelectedIndex(),
            set:         static (ctrl, v) => { if (v >= 0) ctrl.SelectedIndex = v; },
            shouldWrite: static el =>
            {
                int index = el.GetSelectedIndex();
                return index >= 0 && SelectionWriteGuard.CanLand(index, el.ItemCount);
            });
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
        // Issue #1090 — see the ListView twin: reachability guard, not just sign,
        // applied for consistency (this port is selftest-only, not registered).
        .OneWayConditional(
            get:         static el => el.GetSelectedIndex(),
            set:         static (ctrl, v) => { if (v >= 0) ctrl.SelectedIndex = v; },
            shouldWrite: static el =>
            {
                int index = el.GetSelectedIndex();
                return index >= 0 && SelectionWriteGuard.CanLand(index, el.ItemCount);
            });
}
