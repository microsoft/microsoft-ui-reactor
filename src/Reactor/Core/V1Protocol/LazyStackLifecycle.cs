using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// Spec 047 §14 Phase 3 — Lazy*Stack mount/update logic relocated out of the
// shared Reconciler.Mount.cs / Reconciler.Update.cs partials into a V1-owned
// lifecycle class. LazyStackHandler delegates here. The bodies are the verbatim
// legacy MountLazyStack/UpdateLazyStack arms; they call back into the reconciler
// for shared infra (the element pool, the keyed list-state attached property, and
// the shared repeater move-animation helper) rather than duplicating it.
internal static class LazyStackLifecycle
{
    internal static UIElement Mount(Reconciler reconciler, LazyStackElementBase lazy, Action requestRerender)
    {
        var repeater = new WinUI.ItemsRepeater();

        repeater.Layout = new WinUI.StackLayout
        {
            Orientation = lazy.Orientation,
            Spacing = lazy.Spacing,
        };

        // Spec 042 Phase 1: bind the repeater to an internally-owned
        // ObservableCollection<ReactorRow>. Without this, every Items.Count
        // change replaced the int-range source wholesale and the
        // ItemsRepeater re-realized every visible child.
        var listState = BuildListState(lazy);
        Reconciler.SetListState(repeater, listState);
        repeater.ItemsSource = listState.Source;
        var factory = lazy.CreateFactory(reconciler, requestRerender, reconciler._pool);
        // Plumb the list state into the factory so its _mountedElements
        // dictionary is keyed by ReactorRow.Key (reorder-stable) instead
        // of by realized index.
        lazy.AttachListStateToFactory(factory, listState);
        repeater.ItemTemplate = factory;
        Reconciler.SetElementTag(repeater, lazy);
        Reconciler.ApplySetters(lazy.RepeaterSetters, repeater);

        var sv = reconciler._pool.TryRent(typeof(WinUI.ScrollViewer)) as WinUI.ScrollViewer ?? new WinUI.ScrollViewer();
        sv.Content = repeater;
        sv.HorizontalScrollBarVisibility = lazy.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
            ? WinUI.ScrollBarVisibility.Auto
            : WinUI.ScrollBarVisibility.Disabled;
        sv.VerticalScrollBarVisibility = lazy.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Vertical
            ? WinUI.ScrollBarVisibility.Auto
            : WinUI.ScrollBarVisibility.Disabled;
        Reconciler.SetElementTag(sv, lazy);
        Reconciler.ApplySetters(lazy.ScrollViewerSetters, sv);

        return sv;
    }

    internal static UIElement? Update(Reconciler reconciler, LazyStackElementBase n, WinUI.ScrollViewer sv, Action requestRerender)
    {
        if (sv.Content is WinUI.ItemsRepeater repeater)
        {
            // Try to update the existing factory in place. This avoids
            // replacing ItemTemplate, which would cause ItemsRepeater to
            // re-realize all items (modifying Children during layout →
            // "Cannot run layout in the middle of a collection change").
            // The factory keeps its identity; existing realized items
            // stay mounted. On next scroll or layout, IElementFactory.GetElement
            // uses the updated viewBuilder to produce new content.
            if (repeater.ItemTemplate is IElementFactory existingFactory && n.TryUpdateFactory(existingFactory))
            {
                // Spec 042 Phase 1: route Items changes through the keyed
                // diff into the internally-owned OC<ReactorRow>. WinUI
                // sees incremental Insert/Move/RemoveAt events and only
                // animates affected containers; the steady-state
                // RefreshRealizedItems below still runs for per-row
                // content updates.
                ApplyLazyKeyedDiffOrFallback(reconciler, repeater, n, existingFactory);
                n.RefreshRealizedItems(existingFactory, repeater);
            }
            else
            {
                // First mount or type mismatch — full replacement using the
                // Phase 1 OC<ReactorRow> binding shape.
                var fresh = BuildListState(n);
                Reconciler.SetListState(repeater, fresh);
                repeater.ItemsSource = fresh.Source;
                var factory = n.CreateFactory(reconciler, requestRerender, reconciler._pool);
                n.AttachListStateToFactory(factory, fresh);
                repeater.ItemTemplate = factory;
            }
            if (repeater.Layout is WinUI.StackLayout layout)
                layout.Spacing = n.Spacing;
            Reconciler.SetElementTag(repeater, n);
            Reconciler.ApplySetters(n.RepeaterSetters, repeater);
        }
        Reconciler.SetElementTag(sv, n);
        Reconciler.ApplySetters(n.ScrollViewerSetters, sv);
        return null;
    }

    private static void ApplyLazyKeyedDiffOrFallback(Reconciler reconciler, WinUI.ItemsRepeater repeater, LazyStackElementBase n, IElementFactory factory)
    {
        var state = Reconciler.GetListState(repeater);
        if (state is null || !ReferenceEquals(repeater.ItemsSource, state.Source))
        {
            var fresh = BuildListState(n);
            Reconciler.SetListState(repeater, fresh);
            repeater.ItemsSource = fresh.Source;
            n.AttachListStateToFactory(factory, fresh);
            return;
        }

        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new LazyKeyAdapter(n),
            static (item, _) => item.Key,
            reconciler._logger,
            repeater.GetType().Name,
            ambient,
            controlInstance: repeater);

        // ItemsRepeater realizes containers through ElementFactory, so the
        // enter animation runs from there. Moves on already-realized
        // elements need the same handle-based offset animation as the
        // templated list path. (spec 042 §6.)
        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            reconciler.ApplyMoveAnimationsRepeater(repeater, movedRows, ambient.Kind);
        // Bailout reset still mutates state.Source in place, so the
        // existing ItemsSource binding remains valid.
    }

    private readonly struct LazyKeyAdapter : IReadOnlyList<LazyKeyAdapter.KeyOnly>
    {
        private readonly LazyStackElementBase _el;
        public LazyKeyAdapter(LazyStackElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    private static ReactorListState BuildListState(LazyStackElementBase lazy)
    {
        var state = new ReactorListState();
        int n = lazy.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, lazy.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }
}
