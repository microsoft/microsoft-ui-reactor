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
            if (ChangeEchoSuppressor.ShouldSuppress(g)) return;
            if (Reconciler.GetElementTag(g) is not GridViewElement el) return;
            el.OnSelectedIndexChanged?.Invoke(g.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                h(g.SelectedItems.OfType<int>().ToList());
            }
        };
        if (gv.OnItemClick is not null)
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
        if (gv.SelectedIndex >= 0 && gridView.SelectedIndex != gv.SelectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(gridView);
            gridView.SelectedIndex = gv.SelectedIndex;
        }
        Reconciler.ApplySetters(gv.Setters, gridView);
        return gridView;
    }

    public void Update(UpdateContext ctx, GridViewElement o, GridViewElement n, WinUI.GridView gv)
    {
        gv.SelectionMode = n.SelectionMode;
        gv.IsItemClickEnabled = n.OnItemClick is not null;
        if (n.Header is not null) gv.Header = n.Header;
        if (gv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            gv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle) && n.ItemContainerStyle is not null)
            gv.ItemContainerStyle = n.ItemContainerStyle;

        // Issue #495 / #464 — rebuild ItemsSource on Items-array change so
        // WinUI recycles + re-realizes containers (CCC re-fires
        // reconciler.Mount with the new per-item element). The handler has
        // `Children = null` and never reconciles realized child controls
        // itself, so skipping the rebuild would silently freeze visible items
        // when only their content changes
        // (see Issue495_GridView_SameLengthContentChange_RefreshesContainers).
        //
        // WinUI drops SelectedIndex to -1 on ItemsSource reassignment when
        // there's an active selection, firing SelectionChanged(-1). Arm
        // BeginSuppress immediately before the swap so the trampoline's
        // ShouldSuppress gate consumes that transient event. Only arm when
        // there's a selection to clear — else the token strands and
        // swallows the next real user input. Matches the ListView fix.
        if (!ReferenceEquals(o.Items, n.Items))
        {
            if (gv.SelectedIndex >= 0)
                ChangeEchoSuppressor.BeginSuppress(gv);
            gv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();
        }

        Reconciler.SetElementTag(gv, n);

        // SelectionChanged is wired unconditionally in Mount (see comment in
        // ListViewHandler.Update). Tag refresh suffices to pick up a later-attached
        // OnSelectedIndexChanged / OnSelectionChanged.
        if (o.OnItemClick is null && n.OnItemClick is not null)
            gv.ItemClick += (s, args) =>
            {
                var g = (WinUI.GridView)s!;
                if (args.ClickedItem is int idx)
                    (Reconciler.GetElementTag(g) as GridViewElement)?.OnItemClick?.Invoke(idx);
            };

        // Issue #464 — wrap the SelectedIndex write so the deferred
        // SelectionChanged GridView fires after the property set doesn't echo
        // back into OnSelectedIndexChanged. Only arm on real drift to avoid
        // stranding a token for a no-op write (see Mount comment, and
        // ChangeEchoSuppressor.BeginSuppress / ShouldSuppress in
        // src/Reactor/Core/ChangeEchoSuppressor.cs — BeginSuppress always
        // increments, ShouldSuppress only consumes on a real event, so an
        // unconsumed token swallows the next user input).
        if (n.SelectedIndex >= 0 && gv.SelectedIndex != n.SelectedIndex)
        {
            ChangeEchoSuppressor.BeginSuppress(gv);
            gv.SelectedIndex = n.SelectedIndex;
        }
        Reconciler.ApplySetters(n.Setters, gv);
    }

    public ChildrenStrategy<GridViewElement, WinUI.GridView>? Children => null;
}
