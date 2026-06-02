using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// Spec 047 §14 Phase 3 — typed templated-list (ListView/GridView/FlipView<T>)
// mount/update logic relocated out of the shared Reconciler.Mount.cs /
// Reconciler.Update.cs partials into a V1-owned lifecycle class.
// TemplatedListHandler delegates here. The bodies are the verbatim legacy
// MountTemplatedList*/UpdateTemplated* arms; they call back into the reconciler
// for shared realization infra (HandleTemplatedContainerContentChanging,
// RefreshRealizedContainers, ApplyMoveAnimations, the keyed list-state attached
// property, and the shared ContentControl item template) rather than duplicating it.
internal static class TemplatedListLifecycle
{
    internal static UIElement Mount(Reconciler reconciler, TemplatedListElementBase el, Action requestRerender)
    {
        return el.ControlKind switch
        {
            TemplatedControlKind.ListView => MountListView(reconciler, el, requestRerender),
            TemplatedControlKind.GridView => MountGridView(reconciler, el, requestRerender),
            TemplatedControlKind.FlipView => MountFlipView(reconciler, el, requestRerender),
            _ => throw new InvalidOperationException($"Unknown TemplatedControlKind: {el.ControlKind}")
        };
    }

    internal static UIElement? Update(Reconciler reconciler, TemplatedListElementBase o, TemplatedListElementBase n, UIElement control, Action requestRerender)
    {
        return control switch
        {
            WinUI.GridView gv => UpdateGridView(reconciler, o, n, gv, requestRerender),
            WinUI.FlipView fv => UpdateFlipView(reconciler, o, n, fv, requestRerender),
            WinUI.ListView lv => UpdateListView(reconciler, o, n, lv, requestRerender),
            _ => throw new InvalidOperationException(
                $"TemplatedListHandler: unexpected control type {control.GetType().Name} for element {n.GetType().Name}."),
        };
    }

    internal static WinUI.ListView MountListView(Reconciler reconciler, TemplatedListElementBase el, Action requestRerender)
    {
        var listView = new WinUI.ListView
        {
            SelectionMode = el.GetSelectionMode(),
            IsItemClickEnabled = el.GetIsItemClickEnabled(),
        };
        var header = el.GetHeader();
        if (header is not null) listView.Header = header;

        Reconciler.SetElementTag(listView, el);
        listView.ItemTemplate = Reconciler.SharedContentControlTemplate.Value;

        listView.ContainerContentChanging += (sender, args) =>
            reconciler.HandleTemplatedContainerContentChanging(sender, args, requestRerender);

        listView.SelectionChanged += (s, _) =>
        {
            var l = (WinUI.ListView)s!;
            // Issue #495 — consume any pending echo-suppress token before
            // dispatching to the user callback (mirrors the GridView/ListView
            // hand-coded handlers wired in issues #464/#495). Mount/Update
            // arm BeginSuppress around the SelectedIndex writes below so the
            // synthesized SelectionChanged is dropped here instead of looping
            // back through OnSelectedIndexChanged → setState → re-render.
            if (ChangeEchoSuppressor.ShouldSuppress(l)) return;
            if (Reconciler.GetElementTag(l) is not TemplatedListElementBase tel) return;
            tel.InvokeSelectionChanged(l.SelectedIndex);
            if (tel.HasMultiSelectionCallback)
            {
                var snapshot = new List<int>(l.SelectedItems.Count);
                foreach (var item in l.SelectedItems)
                {
                    // Spec 042 Phase 1: items in SelectedItems are
                    // ReactorRow when the OC delta path is active; preserve
                    // the legacy int path for the rare direct-int consumer.
                    if (item is ReactorRow row) snapshot.Add(row.Index);
                    else if (item is int i) snapshot.Add(i);
                }
                tel.InvokeMultiSelectionChanged(snapshot);
            }
        };
        listView.ItemClick += (s, args) =>
        {
            var l = (WinUI.ListView)s!;
            // ClickedItem is the OC element — ReactorRow under the delta
            // path; int under the legacy path. Translate to the data index
            // either way.
            int? idx = args.ClickedItem switch
            {
                ReactorRow row => row.Index,
                int i => i,
                _ => null,
            };
            if (idx is int v)
                (Reconciler.GetElementTag(l) as TemplatedListElementBase)?.InvokeItemClick(v);
        };

        // Spec 042 Phase 1: bind to an internally-owned
        // ObservableCollection<ReactorRow> so insert/remove/move surface
        // as INotifyCollectionChanged deltas — WinUI animates only the
        // affected containers rather than re-realizing the entire viewport.
        var listState = BuildListState(el);
        Reconciler.SetListState(listView, listState);
        listView.ItemsSource = listState.Source;

        var selectedIndex = el.GetSelectedIndex();
        // Issue #495 — wrap initial SelectedIndex write so the deferred
        // SelectionChanged (after container realization) is suppressed.
        // Drift check avoids stranding a token for a no-op write.
        if (selectedIndex >= 0 && listView.SelectedIndex != selectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(listView);
            listView.SelectedIndex = selectedIndex;
        }
        el.ApplyControlSetters(listView);
        return listView;
    }

    internal static WinUI.GridView MountGridView(Reconciler reconciler, TemplatedListElementBase el, Action requestRerender)
    {
        var gridView = new WinUI.GridView
        {
            SelectionMode = el.GetSelectionMode(),
            IsItemClickEnabled = el.GetIsItemClickEnabled(),
        };
        var header = el.GetHeader();
        if (header is not null) gridView.Header = header;

        Reconciler.SetElementTag(gridView, el);
        gridView.ItemTemplate = Reconciler.SharedContentControlTemplate.Value;

        gridView.ContainerContentChanging += (sender, args) =>
            reconciler.HandleTemplatedContainerContentChanging(sender, args, requestRerender);

        gridView.SelectionChanged += (s, _) =>
        {
            var g = (WinUI.GridView)s!;
            // Issue #495 — see ListView trampoline above.
            if (ChangeEchoSuppressor.ShouldSuppress(g)) return;
            if (Reconciler.GetElementTag(g) is not TemplatedListElementBase tel) return;
            tel.InvokeSelectionChanged(g.SelectedIndex);
            if (tel.HasMultiSelectionCallback)
            {
                var snapshot = new List<int>(g.SelectedItems.Count);
                foreach (var item in g.SelectedItems)
                {
                    if (item is ReactorRow row) snapshot.Add(row.Index);
                    else if (item is int i) snapshot.Add(i);
                }
                tel.InvokeMultiSelectionChanged(snapshot);
            }
        };
        gridView.ItemClick += (s, args) =>
        {
            var g = (WinUI.GridView)s!;
            int? idx = args.ClickedItem switch
            {
                ReactorRow row => row.Index,
                int i => i,
                _ => null,
            };
            if (idx is int v)
                (Reconciler.GetElementTag(g) as TemplatedListElementBase)?.InvokeItemClick(v);
        };

        var gridState = BuildListState(el);
        Reconciler.SetListState(gridView, gridState);
        gridView.ItemsSource = gridState.Source;

        var selectedIndex = el.GetSelectedIndex();
        // Issue #495 — see MountListView.
        if (selectedIndex >= 0 && gridView.SelectedIndex != selectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(gridView);
            gridView.SelectedIndex = selectedIndex;
        }
        el.ApplyControlSetters(gridView);
        return gridView;
    }

    internal static WinUI.FlipView MountFlipView(Reconciler reconciler, TemplatedListElementBase el, Action requestRerender)
    {
        var flipView = new WinUI.FlipView();

        Reconciler.SetElementTag(flipView, el);

        // FlipView doesn't support ContainerContentChanging (not a ListViewBase).
        // Pre-mount all items — FlipView typically has few items so this is fine.
        for (int i = 0; i < el.ItemCount; i++)
        {
            var itemElement = el.BuildItemView(i);
            var ctrl = reconciler.Mount(itemElement, requestRerender);
            if (ctrl is not null) flipView.Items.Add(ctrl);
        }

        flipView.SelectionChanged += (s, _) =>
        {
            var f = (WinUI.FlipView)s!;
            // Issue #495 — see ListView trampoline above.
            if (ChangeEchoSuppressor.ShouldSuppress(f)) return;
            (Reconciler.GetElementTag(f) as TemplatedListElementBase)?.InvokeSelectionChanged(f.SelectedIndex);
        };

        var selectedIndex = el.GetSelectedIndex();
        // Issue #495 — see MountListView.
        if (selectedIndex >= 0 && flipView.SelectedIndex != selectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(flipView);
            flipView.SelectedIndex = selectedIndex;
        }
        el.ApplyControlSetters(flipView);
        return flipView;
    }

    internal static UIElement? UpdateListView(Reconciler reconciler, TemplatedListElementBase o, TemplatedListElementBase n, WinUI.ListView lv, Action requestRerender)
    {
        lv.SelectionMode = n.GetSelectionMode();
        lv.IsItemClickEnabled = n.GetIsItemClickEnabled();
        var header = n.GetHeader();
        if (header is not null) lv.Header = header;

        // SetElementTag *before* the diff so HandleTemplatedContainerContentChanging
        // — which fires synchronously inside the OC insert/move ops — reads the
        // new element when materializing freshly-inserted containers.
        Reconciler.SetElementTag(lv, n);

        // Spec 042 Phase 1: feed structural changes through the
        // keyed-diff pipeline so WinUI animates only the affected
        // containers. RefreshRealizedContainers still runs in every case
        // because the viewBuilder is a closure that may produce
        // different content even when the key set is unchanged.
        ApplyKeyedDiffOrFallback(reconciler, lv, n);
        reconciler.RefreshRealizedContainers(lv, n, requestRerender);

        var selectedIndex = n.GetSelectedIndex();
        // Issue #495 — wrap SelectedIndex write so the deferred
        // SelectionChanged doesn't echo into the user callback. Drift check
        // avoids stranding a token on a no-op write.
        if (selectedIndex >= 0 && lv.SelectedIndex != selectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(lv);
            lv.SelectedIndex = selectedIndex;
        }
        n.ApplyControlSetters(lv);
        return null;
    }

    internal static UIElement? UpdateGridView(Reconciler reconciler, TemplatedListElementBase o, TemplatedListElementBase n, WinUI.GridView gv, Action requestRerender)
    {
        gv.SelectionMode = n.GetSelectionMode();
        gv.IsItemClickEnabled = n.GetIsItemClickEnabled();
        var header = n.GetHeader();
        if (header is not null) gv.Header = header;

        Reconciler.SetElementTag(gv, n);
        ApplyKeyedDiffOrFallback(reconciler, gv, n);
        reconciler.RefreshRealizedContainers(gv, n, requestRerender);

        var selectedIndex = n.GetSelectedIndex();
        // Issue #495 — see UpdateListView.
        if (selectedIndex >= 0 && gv.SelectedIndex != selectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(gv);
            gv.SelectedIndex = selectedIndex;
        }
        n.ApplyControlSetters(gv);
        return null;
    }

    /// <summary>
    /// Spec 042 §4 — apply the keyed diff to the control's attached
    /// <see cref="ReactorListState"/>. If the state is missing (control
    /// mounted before Phase 1, or the diff bailed out and the legacy
    /// ItemsSource was swapped in) we transparently rebuild a fresh
    /// state and re-bind ItemsSource. The result is correctness either
    /// way; bailout only degrades the animation.
    /// </summary>
    private static void ApplyKeyedDiffOrFallback(Reconciler reconciler, WinUI.ListViewBase lvb, TemplatedListElementBase n)
    {
        var state = Reconciler.GetListState(lvb);
        if (state is null || !ReferenceEquals(lvb.ItemsSource, state.Source))
        {
            // No state, or another path replaced ItemsSource (bailout from a
            // prior render). Rebuild and rebind in one step.
            var fresh = BuildListState(n);
            Reconciler.SetListState(lvb, fresh);
            lvb.ItemsSource = fresh.Source;
            return;
        }

        // Project new keys through the typed peer. Pass the active ambient
        // so newly-inserted ReactorRows are tagged with the kind and the
        // ContainerContentChanging handler can attach a per-container
        // enter animation as those containers realize. (spec 042 §6.)
        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new TemplatedKeyAdapter(n),
            static (item, _) => item.Key,
            reconciler._logger,
            lvb.GetType().Name,
            ambient,
            controlInstance: lvb);

        // Drive per-container offset animations for survivors that moved.
        // Insert / Remove animations attach through the realize/recycle
        // path so they survive virtualization; Move requires the live
        // container handle here because the row instance was already
        // realized before the move op.
        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            reconciler.ApplyMoveAnimations(lvb, movedRows, ambient.Kind);

        if (stats.Bailout)
        {
            // Reset replaced state.Source's contents in bulk; ItemsSource
            // still references the same OC object so WinUI sees a single
            // Reset action and re-realizes. Acceptable per spec §4.3.
            // No additional binding refresh needed.
        }
    }

    /// <summary>
    /// Adapter so the generic <see cref="KeyedListDiff.Apply{T}"/> can run
    /// against the abstract non-generic <see cref="TemplatedListElementBase"/>
    /// without an extra allocation per item.
    /// </summary>
    private readonly struct TemplatedKeyAdapter : IReadOnlyList<TemplatedKeyAdapter.KeyOnly>
    {
        private readonly TemplatedListElementBase _el;
        public TemplatedKeyAdapter(TemplatedListElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    internal static UIElement? UpdateFlipView(Reconciler reconciler, TemplatedListElementBase o, TemplatedListElementBase n, WinUI.FlipView fv, Action requestRerender)
    {
        // FlipView items are pre-mounted directly (no ContainerContentChanging).
        // Build old element array from o, then reconcile like regular items.
        int oldCount = o.ItemCount;
        int newCount = n.ItemCount;
        int shared = Math.Min(oldCount, newCount);

        for (int i = 0; i < shared; i++)
        {
            var oldItemElement = o.BuildItemView(i);
            var newItemElement = n.BuildItemView(i);
            if (fv.Items[i] is UIElement existingCtrl && reconciler.CanUpdate(oldItemElement, newItemElement))
            {
                var replacement = reconciler.Update(oldItemElement, newItemElement, existingCtrl, requestRerender);
                if (replacement is not null && replacement != existingCtrl)
                    fv.Items[i] = replacement;
            }
            else
            {
                if (fv.Items[i] is UIElement oldCtrl) reconciler.UnmountChild(oldCtrl);
                fv.Items[i] = reconciler.Mount(newItemElement, requestRerender)!;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
        {
            if (fv.Items[i] is UIElement oldCtrl) reconciler.UnmountChild(oldCtrl);
            fv.Items.RemoveAt(i);
        }

        // Add new
        for (int i = shared; i < newCount; i++)
        {
            var ctrl = reconciler.Mount(n.BuildItemView(i), requestRerender);
            if (ctrl is not null) fv.Items.Add(ctrl);
        }

        // Issue #495 — drift-guarded, suppressed write. Previously
        // unconditional, which would write -1 when n.GetSelectedIndex() == -1
        // (the default for TemplatedFlipViewElement<T> is 0, so the prior
        // unconditional write usually matched, but a `with { SelectedIndex = -1 }`
        // would clear silently).
        var fvSelectedIndex = n.GetSelectedIndex();
        if (fv.SelectedIndex != fvSelectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(fv);
            fv.SelectedIndex = fvSelectedIndex;
        }
        Reconciler.SetElementTag(fv, n);
        n.ApplyControlSetters(fv);
        return null;
    }

    /// <summary>
    /// Builds a fresh <see cref="ReactorListState"/> populated with the
    /// element's current keys. Used at mount time and at bulk-replace
    /// bailout time. Tolerates duplicate keys per <see cref="ReactorListState.Reset"/>;
    /// the bailout path is where duplicate-key diagnostics surface (see
    /// <see cref="KeyedListDiff"/>).
    /// </summary>
    private static ReactorListState BuildListState(TemplatedListElementBase el)
    {
        var state = new ReactorListState();
        int n = el.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, el.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }
}
