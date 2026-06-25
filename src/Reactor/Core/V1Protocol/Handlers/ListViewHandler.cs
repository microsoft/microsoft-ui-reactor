using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 — templated items host (V1-owned). WinUI
/// <see cref="WinUI.ListView"/> drives container realization through
/// <c>ContainerContentChanging</c> + a shared <c>DataTemplate</c> +
/// <c>ItemsSource = Range(0..N)</c> for on-demand virtualized mounting.
///
/// <para>This handler owns the full mount/update lifecycle (no children
/// strategy): it installs its own container-realization hook and reads/writes
/// the per-item reactor element via the attached state tag. Realized
/// containers are torn down by the recycle arm of
/// <c>ContainerContentChanging</c>, so the default unmount disposition
/// suffices. <c>Children = null</c> because this handler fully owns child
/// realization.</para>
/// </summary>
internal sealed class ListViewHandler : IElementHandler<ListViewElement, WinUI.ListView>
{
    // #98: per-control ping-pong range source. Keyed weakly on the ListView so
    // the buffers are collected with the control. See RangeSourceState below.
    private static readonly ConditionalWeakTable<WinUI.ListView, RangeSourceState> s_rangeSources = new();

    public WinUI.ListView Mount(MountContext ctx, ListViewElement lv)
    {
        var reconciler = ctx.Reconciler;
        var requestRerender = ctx.RequestRerender;
        var listView = new WinUI.ListView
        {
            SelectionMode = lv.SelectionMode,
            IsItemClickEnabled = lv.OnItemClick is not null,
            IncrementalLoadingTrigger = lv.IncrementalLoadingTrigger,
        };
        if (lv.Header is not null) listView.Header = lv.Header;
        if (lv.ItemContainerStyle is not null) listView.ItemContainerStyle = lv.ItemContainerStyle;

        Reconciler.SetElementTag(listView, lv);

        // DataTemplate with a ContentControl shell — we populate its Content on demand
        listView.ItemTemplate = Reconciler.SharedContentControlTemplate.Value;

        listView.ContainerContentChanging += (sender, args) =>
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer.ContentTemplateRoot is ContentControl oldCc)
                {
                    if (oldCc.Content is UIElement oldCtrl)
                        reconciler.UnmountChild(oldCtrl);
                    oldCc.Content = null;
                }
                return;
            }

            args.Handled = true;
            var items = (Reconciler.GetElementTag((UIElement)sender!) as ListViewElement)?.Items;
            if (items is not null && args.ItemIndex >= 0 && args.ItemIndex < items.Length
                && args.ItemContainer.ContentTemplateRoot is ContentControl cc)
            {
                var ctrl = reconciler.Mount(items[args.ItemIndex], requestRerender);
                cc.Content = ctrl;
            }
        };

        // Subscribe unconditionally so OnSelectionChanged (multi-select snapshot)
        // and OnSelectedIndexChanged (single focused index) both pick up
        // handlers attached on a later record-with without re-subscribing.
        listView.SelectionChanged += (s, _) =>
        {
            var l = (WinUI.ListView)s!;
            // Issue #495 — consume any pending echo-suppress token before
            // dispatching to the user callback (mirrors the GridView trampoline
            // wired in issue #464). The programmatic SelectedIndex writes
            // below in Mount / Update arm the suppressor with BeginSuppress so
            // their synthesized SelectionChanged is dropped here instead of
            // looping back through OnSelectedIndexChanged → setIndex →
            // re-render → … which previously caused a 50+-render storm when
            // the callback was bound to UseState.
            if (!Reconciler.TryGetReactorState(l, out var state)) return;
            if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
            if (state.Element is not ListViewElement el) return;
            el.OnSelectedIndexChanged?.Invoke(l.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                // #100: SelectedItems is IList<object> of int — copy into a
                // typed snapshot with a plain loop instead of
                // OfType<int>().ToList() (an OfType iterator + a growable List
                // allocated per SelectionChanged). Pre-size to the count.
                var sel = l.SelectedItems;
                var copy = new List<int>(sel.Count);
                for (int i = 0; i < sel.Count; i++)
                    if (sel[i] is int v) copy.Add(v);
                h(copy);
            }
        };
        // #110: subscribe ItemClick ONCE at Mount (not gated on OnItemClick),
        // mirroring the SelectionChanged wiring above. IsItemClickEnabled (set
        // from `OnItemClick is not null` in Mount/Update) gates whether WinUI
        // raises the event, and the trampoline reads the live handler from the
        // tag — so a later-attached OnItemClick fires without re-subscribing and
        // a detached one no-ops. The previous null→non-null re-subscribe in
        // Update accumulated duplicate handlers (leak + multi-fire) across
        // record-with cycles.
        listView.ItemClick += (s, args) =>
        {
            var l = (WinUI.ListView)s!;
            if (args.ClickedItem is int idx)
                (Reconciler.GetElementTag(l) as ListViewElement)?.OnItemClick?.Invoke(idx);
        };

        // Set ItemsSource LAST — triggers container creation which needs the handler above
        // #98: ping-pong two cached [0..N-1] lists instead of allocating a fresh
        // Enumerable.Range(...).ToList() every render. RangeSourceState returns a
        // different List reference on each call (so WinUI still sees a reference
        // change and recycles/re-realizes containers — Issue #495), rebuilding
        // the backing buffers in place only when the count changes.
        listView.ItemsSource = s_rangeSources
            .GetValue(listView, static _ => new RangeSourceState())
            .Next(lv.Items.Length);

        // Issue #495 — wrap the initial SelectedIndex write so the deferred
        // SelectionChanged ListView fires after container realization is
        // suppressed instead of leaking into OnSelectedIndexChanged. Only arm
        // on real drift to avoid stranding a token for a no-op write — see
        // ChangeEchoSuppressor.BeginSuppress / ShouldSuppress in
        // src/Reactor/Core/ChangeEchoSuppressor.cs: BeginSuppress always
        // increments, ShouldSuppress only consumes on a real event, so an
        // unconsumed token would swallow the next real user input.
        //
        // Spec 050: Optional.Of(-1) is the explicit force-clear sentinel
        // (see ListViewElement.SelectedIndex XML doc and
        // docs/guide/migration/050-optional-t.md). WinUI accepts -1 as
        // "deselect", so write it through the same drift gate. Optional<int>.Unset
        // (HasValue == false) means "control owns the selection" and falls
        // through without a write.
        if (lv.SelectedIndex is { HasValue: true } mountIndex
            && listView.SelectedIndex != mountIndex.Value)
        {
            ReactorBinding.WriteSuppressed(listView, () => listView.SelectedIndex = mountIndex.Value);
        }
        Reconciler.ApplySetters(lv.Setters, listView);
        return listView;
    }

    public void Update(UpdateContext ctx, ListViewElement o, ListViewElement n, WinUI.ListView lv)
    {
        lv.SelectionMode = n.SelectionMode;
        lv.IsItemClickEnabled = n.OnItemClick is not null;
        if (n.Header is not null) lv.Header = n.Header;
        if (lv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            lv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle) && n.ItemContainerStyle is not null)
            lv.ItemContainerStyle = n.ItemContainerStyle;

        // Issue #495 — when the Items array changes (idiomatic Reactor authors
        // allocate `new Element[] { ... }` literals on every render), rebuild
        // ItemsSource so WinUI recycles + re-realizes its containers and
        // ContainerContentChanging re-fires `reconciler.Mount` with the new
        // per-item element. The handler has `Children = null` and never
        // reconciles realized child controls itself, so skipping the rebuild
        // would silently freeze visible items when only their content changes
        // (see Issue495_ListView_SameLengthContentChange_RefreshesContainers).
        //
        // WinUI synchronously drops SelectedIndex to -1 on ItemsSource
        // reassignment when there's an active selection, and fires
        // SelectionChanged(-1). Arm BeginSuppress immediately before the
        // swap so that transient event is consumed by the trampoline's
        // ShouldSuppress gate instead of looping back through
        // OnSelectedIndexChanged → setState → re-render → swap → … (the
        // 50+-render storm reported in #495). Only arm when there's actually
        // a selection to clear — otherwise the token strands and swallows
        // the next real user input.
        if (!ReferenceEquals(o.Items, n.Items))
        {
            if (lv.SelectedIndex >= 0)
                ChangeEchoSuppressor.BeginSuppress(lv);
            // #98: ping-pong cached range list — see Mount. The returned
            // reference always differs from the currently-assigned ItemsSource,
            // preserving the container recycle/re-realize that Issue #495 needs.
            lv.ItemsSource = s_rangeSources
                .GetValue(lv, static _ => new RangeSourceState())
                .Next(n.Items.Length);
        }

        Reconciler.SetElementTag(lv, n);

        // Mount subscribes SelectionChanged AND ItemClick unconditionally and
        // reads handlers via GetElementTag, so no lazy wire here — the tag
        // refresh above makes a newly-attached OnSelectedIndexChanged /
        // OnSelectionChanged / OnItemClick pick up on the next event. (#110:
        // the previous null→non-null ItemClick re-subscribe leaked handlers.)

        // Issue #495 — wrap the SelectedIndex write so the SelectionChanged
        // ListView fires after the property set doesn't echo back into
        // OnSelectedIndexChanged. Only arm on real drift (see Mount comment
        // above and the GridView analog wired for issue #464). Spec 050: -1
        // is the explicit force-clear sentinel; Unset means "control owns it".
        if (n.SelectedIndex is { HasValue: true } updateIndex
            && lv.SelectedIndex != updateIndex.Value)
        {
            ReactorBinding.WriteSuppressed(lv, () => lv.SelectedIndex = updateIndex.Value);
        }
        Reconciler.ApplySetters(n.Setters, lv);
    }

    public ChildrenStrategy<ListViewElement, WinUI.ListView>? Children => null;
}

/// <summary>
/// #98/#99 — backing store for the <c>ItemsSource = [0..N-1]</c> contract shared
/// by <see cref="ListViewHandler"/> and <see cref="GridViewHandler"/>.
///
/// <para>WinUI's <c>ItemsSource</c> DP setter short-circuits on a
/// reference-equal value, so the same <c>List&lt;int&gt;</c> instance can't be
/// reused to force a container recycle/re-realize (the behaviour Issue #495 /
/// #464 lock down for same-length content changes). Instead of allocating a
/// fresh <c>Enumerable.Range(0, N).ToList()</c> every render, this ping-pongs
/// two buffers: each <see cref="Next"/> call returns the buffer that was
/// <i>not</i> returned last time, so the reference always changes while the
/// content stays <c>[0, 1, …, count-1]</c>. Buffers are rebuilt in place
/// (capacity retained) only when the requested count differs from what that
/// buffer last held, so steady-state renders allocate nothing.</para>
///
/// <para>Only the dormant buffer is ever rebuilt: <see cref="Next"/> toggles
/// before filling, so the buffer currently assigned to <c>ItemsSource</c>
/// (the one returned last call) is never mutated while WinUI observes it.
/// Pure C# (no WinUI dependency) so it is directly unit-testable.</para>
/// </summary>
internal sealed class RangeSourceState
{
    private readonly List<int> _a = new();
    private readonly List<int> _b = new();
    private int _countA = -1;
    private int _countB = -1;
    private bool _useA;

    /// <summary>
    /// Returns a <c>List&lt;int&gt;</c> holding <c>[0 … count-1]</c>. Consecutive
    /// calls return alternating buffer references (never the same reference
    /// twice in a row), so assigning the result to <c>ItemsSource</c> always
    /// changes the reference.
    /// </summary>
    public List<int> Next(int count)
    {
        _useA = !_useA;
        if (_useA)
        {
            Fill(_a, ref _countA, count);
            return _a;
        }
        Fill(_b, ref _countB, count);
        return _b;
    }

    private static void Fill(List<int> buffer, ref int builtCount, int count)
    {
        if (builtCount == count) return;
        buffer.Clear();
        if (buffer.Capacity < count) buffer.Capacity = count;
        for (int i = 0; i < count; i++) buffer.Add(i);
        builtCount = count;
    }
}
