using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 — GridView host (V1-owned). Mirrors
/// <see cref="ListViewHandler"/>: installs the same
/// <c>ItemsSource = Range(0..N) + shared ItemTemplate + ContainerContentChanging</c>
/// lazy container-realization contract (the <c>GridViewDescriptor</c>'s
/// <c>ItemsHost&lt;&gt;</c> strategy is intentionally <i>not</i> registered — it
/// pre-mounts every item with no virtualization, diverging from this behavior).
///
/// <para><c>Children = null</c> because this handler fully owns child
/// realization. Realized containers are torn down by the recycle arm of
/// <c>ContainerContentChanging</c>, so the default unmount disposition
/// suffices.</para>
/// </summary>
internal sealed class GridViewHandler : IElementHandler<GridViewElement, WinUI.GridView>
{
    public WinUI.GridView Mount(MountContext ctx, GridViewElement gv)
    {
        var reconciler = ctx.Reconciler;
        var requestRerender = ctx.RequestRerender;
        var gridView = new WinUI.GridView
        {
            SelectionMode = gv.SelectionMode,
            IsItemClickEnabled = gv.OnItemClick is not null,
            IncrementalLoadingTrigger = gv.IncrementalLoadingTrigger,
        };
        if (gv.Header is not null) gridView.Header = gv.Header;
        if (gv.ItemContainerStyle is not null) gridView.ItemContainerStyle = gv.ItemContainerStyle;

        Reconciler.SetElementTag(gridView, gv);

        gridView.ItemTemplate = Reconciler.SharedContentControlTemplate.Value;

        gridView.ContainerContentChanging += (sender, args) =>
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
            var items = (Reconciler.GetElementTag((UIElement)sender!) as GridViewElement)?.Items;
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

        gridView.SelectionChanged += (s, _) =>
        {
            var g = (WinUI.GridView)s!;
            // Issue #464 — consume any pending echo-suppress token before
            // dispatching to the user callback. The trampoline must check
            // ShouldSuppress in the same shape as every other value-control
            // (CheckBox/Slider/TextBox/etc.) so the programmatic SelectedIndex
            // writes below in Mount / Update don't echo back into
            // OnSelectedIndexChanged.
            if (!Reconciler.TryGetReactorState(g, out var state)) return;
            if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
            if (state.Element is not GridViewElement el) return;
            el.OnSelectedIndexChanged?.Invoke(g.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                h(g.SelectedItems.OfType<int>().ToList());
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
        gridView.ItemClick += (s, args) =>
        {
            var g = (WinUI.GridView)s!;
            if (args.ClickedItem is int idx)
                (Reconciler.GetElementTag(g) as GridViewElement)?.OnItemClick?.Invoke(idx);
        };

        gridView.ItemsSource = Enumerable.Range(0, gv.Items.Length).ToList();

        // Issue #464 — wrap the initial SelectedIndex write so the deferred
        // SelectionChanged that GridView fires after container realization is
        // suppressed instead of leaking into OnSelectedIndexChanged. Only
        // arm when the value would actually drift (a no-op write raises no
        // echo and would strand a token that swallows the next real input).
        //
        // Spec 050: Optional.Of(-1) is the explicit force-clear sentinel
        // (see GridViewElement.SelectedIndex XML doc and
        // docs/guide/migration/050-optional-t.md). WinUI accepts -1 as
        // "deselect", so write it through the same drift gate. Optional<int>.Unset
        // (HasValue == false) means "control owns the selection" and falls
        // through without a write.
        // Issue #1090 — the drift gate alone is not enough: a write of an index
        // that does not exist in the CURRENT ItemsSource (common on mount, while
        // the items list is still empty) cannot be honored by WinUI. No
        // SelectionChanged is raised, so the token armed by WriteSuppressed
        // strands and later swallows the user's first real selection. Only write
        // when the index is reachable; the Update that brings the items in
        // performs the write instead, where it lands and echoes normally.
        if (gv.SelectedIndex is { HasValue: true } mountIndex
            && gridView.SelectedIndex != mountIndex.Value
            && SelectionWriteGuard.CanLand(mountIndex.Value, gv.Items.Length))
        {
            ReactorBinding.WriteSuppressed(gridView, () => gridView.SelectedIndex = mountIndex.Value);
        }
        Reconciler.ApplySetters(gv.Setters, gridView);
        return gridView;
    }

    public void Update(UpdateContext ctx, GridViewElement o, GridViewElement n, WinUI.GridView gv)
    {
        gv.SelectionMode = n.SelectionMode;
        gv.IsItemClickEnabled = n.OnItemClick is not null;
        // Issue #845 — gate on CHANGE, not non-null presence, so a present→null
        // transition clears the property on the control. Header/ItemContainerStyle
        // raise no WinUI events (unlike SelectedIndex), so no echo-suppression is
        // needed and a plain reference-change gate is correct. WinUI accepts null
        // for both (Header=null removes the header; ItemContainerStyle=null resets
        // to the default container style).
        if (!ReferenceEquals(o.Header, n.Header)) gv.Header = n.Header;
        if (gv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            gv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle))
            gv.ItemContainerStyle = n.ItemContainerStyle;

        // Issue #495 / #464 — rebuild ItemsSource on Items-array change so
        // WinUI recycles + re-realizes containers (CCC re-fires
        // reconciler.Mount with the new per-item element). The handler has
        // `Children = null` and never reconciles realized child controls
        // itself, so skipping the rebuild would silently freeze visible items
        // when only their content changes
        // (see Issue495_GridView_SameLengthContentChange_RefreshesContainers).
        //
        // WinUI resets SelectedIndex to -1 on ItemsSource reassignment when
        // there is an active selection, firing SelectionChanged(-1). Arm
        // BeginSuppress immediately before the swap so the trampoline's
        // ShouldSuppress gate consumes that transient event. Only arm when
        // there is a selection to clear — else the token strands and swallows
        // the next real user input. Matches the ListView handler.
        if (!ReferenceEquals(o.Items, n.Items))
        {
            if (gv.SelectedIndex >= 0)
                ChangeEchoSuppressor.BeginSuppress(gv);
            gv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();
        }

        Reconciler.SetElementTag(gv, n);

        // SelectionChanged and ItemClick are both wired unconditionally in Mount
        // (see comment in ListViewHandler.Update). Tag refresh suffices to pick up a
        // later-attached OnSelectedIndexChanged / OnSelectionChanged / OnItemClick.
        // Re-subscribing ItemClick on a null→present transition here would leak a
        // second live handler (never removed on present→null), so a single click
        // would fire the callback twice (issue #779).

        // Issue #464 — wrap the SelectedIndex write so the deferred
        // SelectionChanged GridView fires after the property set doesn't echo
        // back into OnSelectedIndexChanged. Only arm on real drift to avoid
        // stranding a token for a no-op write (see Mount comment, and
        // ChangeEchoSuppressor.BeginSuppress / ShouldSuppress in
        // src/Reactor/Core/ChangeEchoSuppressor.cs — BeginSuppress always
        // increments, ShouldSuppress only consumes on a real event, so an
        // unconsumed token swallows the next user input). Spec 050: -1 is
        // the explicit force-clear sentinel; Unset means "control owns it".
        // Issue #1090 — same reachability guard as Mount: a write WinUI cannot
        // honor (index past the end of the current source) raises no event, so
        // arming for it strands a token that later eats a real selection.
        if (n.SelectedIndex is { HasValue: true } updateIndex
            && gv.SelectedIndex != updateIndex.Value
            && SelectionWriteGuard.CanLand(updateIndex.Value, n.Items.Length))
        {
            ReactorBinding.WriteSuppressed(gv, () => gv.SelectedIndex = updateIndex.Value);
        }
        Reconciler.ApplySetters(n.Setters, gv);
    }

    public ChildrenStrategy<GridViewElement, WinUI.GridView>? Children => null;
}
