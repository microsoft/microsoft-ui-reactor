using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 close-out — descriptor variant of the hand-coded
/// <c>MountRelativePanel</c> / <c>UpdateRelativePanel</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para>Uses the <see cref="Panel{TElement,TControl}"/> strategy with
/// <see cref="Panel{TElement,TControl}.PerChildAttachedAfterAll"/> — the
/// two-pass shape engineered in Batch (1) of this close-out. Mount /
/// Update fire the after-all callback once the engine has mounted every
/// child; the callback builds a name → control map across siblings,
/// then writes the WinUI <see cref="WinUI.RelativePanel"/> attached DPs
/// (<c>RightOf</c>, <c>Below</c>, <c>AlignLeftWith</c>, etc.) that
/// reference siblings by name. Closes the Batch E carve-out.</para>
/// </summary>
internal static class RelativePanelDescriptor
{
    private static readonly Panel<RelativePanelElement, WinUI.RelativePanel> ChildrenStrategy =
        new Panel<RelativePanelElement, WinUI.RelativePanel>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children)
        {
            PerChildAttachedAfterAll = ApplyRelativePanelAttachedProps,
        };

    public static readonly ControlDescriptor<RelativePanelElement, WinUI.RelativePanel> Descriptor =
        new ControlDescriptor<RelativePanelElement, WinUI.RelativePanel>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        };

    private static void ApplyRelativePanelAttachedProps(
        WinUI.RelativePanel panel,
        IReadOnlyList<(UIElement Mounted, Element ChildElement)> pairs)
    {
        // Pass 0 (clear): the Panel<> strategy reconciles children with keyed
        // identity (spec 047 §14), so a control can be reused across renders
        // with a different — or absent — RelativePanelAttached. Clear every
        // RelativePanel attached DP and reset Name first so stale sibling refs,
        // panel-alignment flags, and names from a previous render can't survive.
        // Legacy UpdateRelativePanel avoided this by rebuilding on any child-count
        // change and reconciling positionally; the keyed descriptor path must
        // clear explicitly. Also build the name → control map for pass 1.
        var nameMap = new Dictionary<string, UIElement>(pairs.Count, StringComparer.Ordinal);
        for (int i = 0; i < pairs.Count; i++)
        {
            var (mounted, child) = pairs[i];
            ClearRelativePanelAttached(mounted);

            var rpa = child.GetAttached<RelativePanelAttached>();
            if (mounted is FrameworkElement fe)
                fe.Name = rpa?.Name ?? string.Empty;
            if (rpa is not null)
                nameMap[rpa.Name] = mounted;
        }

        // Pass 1: apply sibling-referencing attached DPs + panel-alignment flags.
        // Mirrors legacy UpdateRelativePanel (Reconciler.Update.cs ~:1348) — the
        // six panel-alignment flags are written unconditionally (Pass 0 already
        // reset them to false, so present-only writes would suffice; unconditional
        // writes keep the parity with legacy explicit).
        for (int i = 0; i < pairs.Count; i++)
        {
            var (mounted, child) = pairs[i];
            var rpa = child.GetAttached<RelativePanelAttached>();
            if (rpa is null) continue;

            if (rpa.RightOf is not null && nameMap.TryGetValue(rpa.RightOf, out var rightOf))
                WinUI.RelativePanel.SetRightOf(mounted, rightOf);
            if (rpa.Below is not null && nameMap.TryGetValue(rpa.Below, out var below))
                WinUI.RelativePanel.SetBelow(mounted, below);
            if (rpa.LeftOf is not null && nameMap.TryGetValue(rpa.LeftOf, out var leftOf))
                WinUI.RelativePanel.SetLeftOf(mounted, leftOf);
            if (rpa.Above is not null && nameMap.TryGetValue(rpa.Above, out var above))
                WinUI.RelativePanel.SetAbove(mounted, above);
            if (rpa.AlignLeftWith is not null && nameMap.TryGetValue(rpa.AlignLeftWith, out var alw))
                WinUI.RelativePanel.SetAlignLeftWith(mounted, alw);
            if (rpa.AlignRightWith is not null && nameMap.TryGetValue(rpa.AlignRightWith, out var arw))
                WinUI.RelativePanel.SetAlignRightWith(mounted, arw);
            if (rpa.AlignTopWith is not null && nameMap.TryGetValue(rpa.AlignTopWith, out var atw))
                WinUI.RelativePanel.SetAlignTopWith(mounted, atw);
            if (rpa.AlignBottomWith is not null && nameMap.TryGetValue(rpa.AlignBottomWith, out var abw))
                WinUI.RelativePanel.SetAlignBottomWith(mounted, abw);
            if (rpa.AlignHorizontalCenterWith is not null && nameMap.TryGetValue(rpa.AlignHorizontalCenterWith, out var ahcw))
                WinUI.RelativePanel.SetAlignHorizontalCenterWith(mounted, ahcw);
            if (rpa.AlignVerticalCenterWith is not null && nameMap.TryGetValue(rpa.AlignVerticalCenterWith, out var avcw))
                WinUI.RelativePanel.SetAlignVerticalCenterWith(mounted, avcw);

            WinUI.RelativePanel.SetAlignLeftWithPanel(mounted, rpa.AlignLeftWithPanel);
            WinUI.RelativePanel.SetAlignRightWithPanel(mounted, rpa.AlignRightWithPanel);
            WinUI.RelativePanel.SetAlignTopWithPanel(mounted, rpa.AlignTopWithPanel);
            WinUI.RelativePanel.SetAlignBottomWithPanel(mounted, rpa.AlignBottomWithPanel);
            WinUI.RelativePanel.SetAlignHorizontalCenterWithPanel(mounted, rpa.AlignHorizontalCenterWithPanel);
            WinUI.RelativePanel.SetAlignVerticalCenterWithPanel(mounted, rpa.AlignVerticalCenterWithPanel);
        }
    }

    // Reset every RelativePanel attached DP to its default so a pool-rented or
    // keyed-reused control never inherits stale positioning from a prior render.
    private static void ClearRelativePanelAttached(UIElement ctrl)
    {
        ctrl.ClearValue(WinUI.RelativePanel.RightOfProperty);
        ctrl.ClearValue(WinUI.RelativePanel.BelowProperty);
        ctrl.ClearValue(WinUI.RelativePanel.LeftOfProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AboveProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignLeftWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignRightWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignTopWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignBottomWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignHorizontalCenterWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignVerticalCenterWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignLeftWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignRightWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignTopWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignBottomWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignHorizontalCenterWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignVerticalCenterWithPanelProperty);
    }
}

/// <summary>
/// Spec 048 §7 — thin <see cref="DescriptorHandler{TElement,TControl}"/>
/// subclass so the Reactor.Factories DSL can reach this descriptor via
/// the <c>Reg&lt;&gt;</c> registration touch without leaking
/// <c>DescriptorHandler&lt;,&gt;</c> as a public surface.
/// </summary>
internal sealed class RelativePanelDescriptorHandler() : DescriptorHandler<RelativePanelElement, WinUI.RelativePanel>(RelativePanelDescriptor.Descriptor);
