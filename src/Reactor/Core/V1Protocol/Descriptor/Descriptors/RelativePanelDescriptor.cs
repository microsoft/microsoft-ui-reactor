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
        // Pass 1: assign FrameworkElement.Name from RelativePanelAttached.Name
        // so the second-pass map lookups below resolve. Build the name → control
        // map at the same time.
        var nameMap = new Dictionary<string, UIElement>(pairs.Count, StringComparer.Ordinal);
        for (int i = 0; i < pairs.Count; i++)
        {
            var (mounted, child) = pairs[i];
            var rpa = child.GetAttached<RelativePanelAttached>();
            if (rpa is null) continue;
            if (mounted is FrameworkElement fe) fe.Name = rpa.Name;
            nameMap[rpa.Name] = mounted;
        }

        // Pass 2: walk again and apply sibling-referencing attached DPs.
        // Mirrors legacy MountRelativePanel (Reconciler.Mount.cs ~:3440).
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

            if (rpa.AlignLeftWithPanel) WinUI.RelativePanel.SetAlignLeftWithPanel(mounted, true);
            if (rpa.AlignRightWithPanel) WinUI.RelativePanel.SetAlignRightWithPanel(mounted, true);
            if (rpa.AlignTopWithPanel) WinUI.RelativePanel.SetAlignTopWithPanel(mounted, true);
            if (rpa.AlignBottomWithPanel) WinUI.RelativePanel.SetAlignBottomWithPanel(mounted, true);
            if (rpa.AlignHorizontalCenterWithPanel) WinUI.RelativePanel.SetAlignHorizontalCenterWithPanel(mounted, true);
            if (rpa.AlignVerticalCenterWithPanel) WinUI.RelativePanel.SetAlignVerticalCenterWithPanel(mounted, true);
        }
    }
}
