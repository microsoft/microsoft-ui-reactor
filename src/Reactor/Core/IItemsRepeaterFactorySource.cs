using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

// Spec 047 §14 Phase 3 finish — Engine (1).
//
// Sibling to Internal.IKeyedItemSource for the ItemsRepeater dispatch arm
// in `Reconciler.BindErasedKeyedItemsSource`. The erased binder pulls
// keys + counts through IKeyedItemSource (the same shape ListViewBase
// uses); for ItemsRepeater hosts it ALSO needs a factory closure plus
// the StackLayout knobs the repeater requires before any container
// realizes. Those are not naturally part of an "item source," so they
// live on this companion interface.
//
// Implementation contract: the source object handed to the binder
// (typically the element itself — see `LazyStackElementBase`) implements
// IKeyedItemSource AND this interface. The binder casts to find the
// factory-side methods. Internal because the abstract layout knobs +
// IElementFactory contract are not part of the public V1 PREVIEW surface
// (`ReactorListState` is internal). Promote to public if a descriptor
// author needs to host a new ItemsRepeater-backed control without
// inheriting `LazyStackElementBase`.

internal interface IItemsRepeaterFactorySource
{
    /// <summary>
    /// Configure the host repeater's layout knobs (Orientation, Spacing,
    /// etc.) at first mount AND on every Update. Spacing in particular
    /// can change between renders without re-creating the factory.
    /// Mirrors the inline WinUI.StackLayout assignment in the legacy
    /// MountLazyStack body (Reconciler.Mount.cs ~:3148).
    /// </summary>
    void ConfigureLayout(WinUI.ItemsRepeater repeater);

    /// <summary>
    /// Produce a fresh <see cref="IElementFactory"/> closure that knows
    /// how to realize element index N into a UIElement subtree. Called on
    /// first mount AND whenever the existing factory's type no longer
    /// matches (e.g. element re-keyed to a different TItem).
    /// </summary>
    IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool);

    /// <summary>
    /// Plumb the host's <c>ReactorListState</c> into the factory so its
    /// element-tracking dictionary is keyed by stable
    /// <c>ReactorRow.Key</c> instead of realized index — same reorder
    /// stability spec 042 Phase 1 added to the legacy lazy-stack path.
    /// </summary>
    void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState);

    /// <summary>
    /// On Update, try to swap the new items + viewBuilder closures into
    /// the existing factory without re-creating it. Returns true on
    /// success; false signals the binder to fall back to a full factory
    /// replacement (which forces ItemsRepeater to re-realize every
    /// visible row).
    /// </summary>
    bool TryUpdateFactory(IElementFactory factory);

    /// <summary>
    /// After a successful in-place update, reconcile every already-
    /// realized container against the new viewBuilder so steady-state
    /// per-row content updates land without a structural change.
    /// </summary>
    void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater);
}
