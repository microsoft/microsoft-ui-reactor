using System;
using System.Collections.Generic;
using System.Linq;
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
            if (ChangeEchoSuppressor.ShouldSuppress(l)) return;
            if (Reconciler.GetElementTag(l) is not ListViewElement el) return;
            el.OnSelectedIndexChanged?.Invoke(l.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                // SelectedItems is List<object> of int — copy into a typed snapshot.
                h(l.SelectedItems.OfType<int>().ToList());
            }
        };
        if (lv.OnItemClick is not null)
            listView.ItemClick += (s, args) =>
            {
                var l = (WinUI.ListView)s!;
                if (args.ClickedItem is int idx)
                    (Reconciler.GetElementTag(l) as ListViewElement)?.OnItemClick?.Invoke(idx);
            };

        // Set ItemsSource LAST — triggers container creation which needs the handler above
        listView.ItemsSource = Enumerable.Range(0, lv.Items.Length).ToList();

        // Issue #495 — wrap the initial SelectedIndex write so the deferred
        // SelectionChanged ListView fires after container realization is
        // suppressed instead of leaking into OnSelectedIndexChanged. Only arm
        // on real drift to avoid stranding a token for a no-op write — see
        // ChangeEchoSuppressor.BeginSuppress / ShouldSuppress in
        // src/Reactor/Core/ChangeEchoSuppressor.cs: BeginSuppress always
        // increments, ShouldSuppress only consumes on a real event, so an
        // unconsumed token would swallow the next real user input.
        if (lv.SelectedIndex >= 0 && listView.SelectedIndex != lv.SelectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(listView);
            listView.SelectedIndex = lv.SelectedIndex;
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
            lv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();
        }

        Reconciler.SetElementTag(lv, n);

        // Mount subscribes SelectionChanged unconditionally and reads handlers
        // via GetElementTag, so no lazy wire here — the tag refresh above
        // makes a newly-attached OnSelectedIndexChanged / OnSelectionChanged
        // pick up on the very next selection.
        if (o.OnItemClick is null && n.OnItemClick is not null)
            lv.ItemClick += (s, args) =>
            {
                var l = (WinUI.ListView)s!;
                if (args.ClickedItem is int idx)
                    (Reconciler.GetElementTag(l) as ListViewElement)?.OnItemClick?.Invoke(idx);
            };

        // Issue #495 — wrap the SelectedIndex write so the SelectionChanged
        // ListView fires after the property set doesn't echo back into
        // OnSelectedIndexChanged. Only arm on real drift (see Mount comment
        // above and the GridView analog wired for issue #464).
        if (n.SelectedIndex >= 0 && lv.SelectedIndex != n.SelectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(lv);
            lv.SelectedIndex = n.SelectedIndex;
        }
        Reconciler.ApplySetters(n.Setters, lv);
    }

    public ChildrenStrategy<ListViewElement, WinUI.ListView>? Children => null;
}
