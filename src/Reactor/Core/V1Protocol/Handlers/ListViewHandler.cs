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
                Reconciler.PropagateItemAutomationName(args.ItemContainer, null);
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
                // Issue #951 — keep .AutomationName(...) on an item view working
                // the same way it does on the keyed overload.
                Reconciler.PropagateItemAutomationName(args.ItemContainer, ctrl);
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
                // SelectedItems is List<object> of int — copy into a typed snapshot.
                h(l.SelectedItems.OfType<int>().ToList());
            }
        };
        // Issue #779 — subscribe unconditionally (mirrors SelectionChanged above)
        // so a later record-with that attaches OnItemClick is picked up without a
        // second subscription. The trampoline no-ops when the current element's
        // OnItemClick is null, and IsItemClickEnabled (set on mount + every update)
        // gates whether WinUI raises ItemClick at all — so exactly one subscription
        // for the control's lifetime fires the callback once per click across any
        // toggle sequence. A conditional mount + Update-time re-subscribe (the old
        // shape) leaked a second live handler on present→null→present.
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
        //
        // Spec 050: Optional.Of(-1) is the explicit force-clear sentinel
        // (see ListViewElement.SelectedIndex XML doc and
        // docs/guide/migration/050-optional-t.md). WinUI accepts -1 as
        // "deselect", so write it through the same drift gate. Optional<int>.Unset
        // (HasValue == false) means "control owns the selection" and falls
        // through without a write.
        //
        // Issue #1090 — the drift gate is not sufficient on its own. A write of
        // an index that does not exist in the CURRENT ItemsSource (very common
        // on mount: the list is still empty while its data loads) cannot be
        // honored by WinUI. The control stays at -1, no SelectionChanged is
        // raised, and the token armed by WriteSuppressed strands — later
        // swallowing the user's first real selection. Only write when the index
        // is actually reachable; when it is not, the subsequent Update that
        // brings the items in performs the write instead, at which point it
        // lands and echoes normally.
        if (lv.SelectedIndex is { HasValue: true } mountIndex
            && listView.SelectedIndex != mountIndex.Value
            && IsReachable(mountIndex.Value, lv.Items.Length))
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
        // Issue #845 — gate on CHANGE, not non-null presence, so a present→null
        // transition clears the property on the control. Header/ItemContainerStyle
        // raise no WinUI events (unlike SelectedIndex), so no echo-suppression is
        // needed and a plain reference-change gate is correct. WinUI accepts null
        // for both (Header=null removes the header; ItemContainerStyle=null resets
        // to the default container style).
        if (!ReferenceEquals(o.Header, n.Header)) lv.Header = n.Header;
        if (lv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            lv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle))
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
        // WinUI's behavior on ItemsSource reassignment is VERSION-DEPENDENT, and
        // the suppression must not depend on which way it goes:
        //
        //   * The runtime bundled with Windows App SDK 2.1.x resets
        //     SelectedIndex to -1 synchronously inside the assignment and fires
        //     SelectionChanged(-1) — measured by the Issue1090_Probe_* selftest.
        //     Something must consume that echo or it loops back through
        //     OnSelectedIndexChanged → setState → re-render → swap → … (the
        //     50+-render storm reported in #495).
        //   * Newer runtimes can PRESERVE a still-valid selection and raise
        //     nothing at all (issue #1090, reported against installed runtime
        //     2.3.1). A token armed unconditionally on the older assumption is
        //     then never consumed, and ShouldSuppress eats the user's next
        //     genuine SelectionChanged instead.
        //
        // Arming unconditionally breaks on the second; not arming breaks on the
        // first. So arm, then OBSERVE: SelectedIndex reads back its post-swap
        // value synchronously with the assignment even when the event itself is
        // queued (also pinned by the probe fixture), so an unchanged index
        // proves no drop happened and the token can be handed back. A deferred
        // drop still shows the changed index, so its token is correctly kept
        // until the queued event arrives.
        //
        // CancelIfUnconsumed is baseline-guarded and therefore safe to call even
        // in the branch where the echo already fired and consumed the token.
        if (!ReferenceEquals(o.Items, n.Items))
        {
            int selectionBeforeSwap = lv.SelectedIndex;
            var arm = selectionBeforeSwap >= 0
                ? ChangeEchoSuppressor.BeginSuppressCancelable(lv)
                : default;

            lv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();

            if (lv.SelectedIndex == selectionBeforeSwap)
                arm.CancelIfUnconsumed();
        }

        Reconciler.SetElementTag(lv, n);

        // Mount subscribes both SelectionChanged and ItemClick unconditionally and
        // reads handlers via GetElementTag, so no lazy wire here — the tag refresh
        // above makes a newly-attached OnSelectedIndexChanged / OnSelectionChanged /
        // OnItemClick pick up on the very next event. Re-subscribing ItemClick on a
        // null→present transition here would leak a second live handler (never
        // removed on present→null), so a single click would fire the callback twice
        // (issue #779).

        // Issue #495 — wrap the SelectedIndex write so the SelectionChanged
        // ListView fires after the property set doesn't echo back into
        // OnSelectedIndexChanged. Only arm on real drift (see Mount comment
        // above and the GridView analog wired for issue #464). Spec 050: -1
        // is the explicit force-clear sentinel; Unset means "control owns it".
        //
        // Issue #1090 — same reachability guard as Mount: a write WinUI cannot
        // honor (index past the end of the current source) raises no event, so
        // arming for it strands a token that later eats a real selection.
        if (n.SelectedIndex is { HasValue: true } updateIndex
            && lv.SelectedIndex != updateIndex.Value
            && IsReachable(updateIndex.Value, n.Items.Length))
        {
            ReactorBinding.WriteSuppressed(lv, () => lv.SelectedIndex = updateIndex.Value);
        }
        Reconciler.ApplySetters(n.Setters, lv);
    }

    /// <summary>
    /// Issue #1090 — can a controlled <c>SelectedIndex</c> write of
    /// <paramref name="index"/> actually land on a source of
    /// <paramref name="itemCount"/> items?
    ///
    /// <para>WinUI will not honor a selection past the end of its
    /// <c>ItemsSource</c> — the property stays where it was (and the set can even
    /// throw) so no <c>SelectionChanged</c> is raised. Suppression is armed per
    /// <em>expected event</em>, so arming for a write that cannot land leaves a
    /// token behind that swallows the user's next genuine selection. The common
    /// trigger is an items list that is still empty on mount while its data
    /// loads.</para>
    ///
    /// <para><c>-1</c> is always reachable: it is the explicit force-clear
    /// sentinel (spec 050), and clearing a selection is meaningful on any source.
    /// The caller's drift gate has already established the control is not
    /// already at -1, so the write will land and echo.</para>
    /// </summary>
    private static bool IsReachable(int index, int itemCount)
        => index < 0 || index < itemCount;

    public ChildrenStrategy<ListViewElement, WinUI.ListView>? Children => null;
}
