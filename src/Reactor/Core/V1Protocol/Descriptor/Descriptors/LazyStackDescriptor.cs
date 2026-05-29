using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (6) descriptor variant of the
/// hand-coded <c>MountLazyStack</c> / <c>UpdateLazyStack</c> arms.
///
/// <para>Registers against the non-generic intermediate base
/// <see cref="LazyStackElementBase"/>, so every closed-T variant
/// (<see cref="LazyVStackElement{T}"/>, <see cref="LazyHStackElement{T}"/>)
/// routes through this one descriptor via the v1 registry's base-derived
/// fallback walk. The strategy is <see cref="TemplatedItemsErased{TElement,TControl}"/>
/// — items + keys + per-item view builder flow through the element's
/// <see cref="IKeyedItemSource"/> implementation; the ItemsRepeater arm
/// in <see cref="Reconciler.BindErasedKeyedItemsSource"/> additionally
/// reads the factory + layout knobs through the element's
/// <see cref="IItemsRepeaterFactorySource"/> implementation. The
/// descriptor itself is non-generic in TItem.</para>
///
/// <para><b>Behavior difference vs hand-coded handler:</b> the legacy
/// <c>MountLazyStack</c> wraps the <see cref="WinUI.ItemsRepeater"/> in a
/// <see cref="WinUI.ScrollViewer"/> with orientation-appropriate
/// scrollbars and applies the element's <c>ScrollViewerSetters</c> to
/// that wrapper. The descriptor port uses <see cref="WinUI.ItemsRepeater"/>
/// directly as <c>TControl</c> — the descriptor's
/// <c>RentControl</c> returns a single <c>TControl</c>, so there is no
/// equivalent place to install the wrapping ScrollViewer. Authors who
/// need scrolling under the descriptor port wrap externally (e.g. by
/// composing the LazyVStack inside a <c>ScrollViewer</c> element). This
/// is acceptable for V1 PREVIEW; promoting to a stable surface will need
/// a "compound-host" descriptor extension. <c>ScrollViewerSetters</c> on
/// the element is therefore inert under the descriptor port.</para>
/// </summary>
internal static class LazyStackDescriptor
{
    public static readonly ControlDescriptor<LazyStackElementBase, WinUI.ItemsRepeater> Descriptor =
        new ControlDescriptor<LazyStackElementBase, WinUI.ItemsRepeater>
        {
            // Routes through Reconciler.BindErasedKeyedItemsSource → the
            // ItemsRepeater arm (Engine (1)), which casts the source to
            // IItemsRepeaterFactorySource for ConfigureLayout / CreateFactory
            // / AttachListStateToFactory / TryUpdateFactory /
            // RefreshRealizedItems. Both interfaces are implemented on
            // LazyStackElementBase itself, so the cast is the same object.
            Children = new TemplatedItemsErased<LazyStackElementBase, WinUI.ItemsRepeater>(
                GetSource: static el => (IKeyedItemSource)el),
            GetSetters = static el => el.RepeaterSetters is { Length: > 0 } s
                ? s
                : global::System.Array.Empty<global::System.Action<WinUI.ItemsRepeater>>(),
        };
}
