using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.5 (anchor only) — side-strip composition.
//
//  Renders the four side strips (Left / Top / Right / Bottom) around the
//  center content. The strip is a thin column / row of buttons, one per
//  pinned tool window, that — when clicked — *will* open the Side popup
//  (lands fully in §2.5). For now the click handler is a no-op so the
//  strip exists as a visible anchor for keyboard navigation and AT.
//
//  Translates WinUI.Dock's Sidebar / SidePopup composition:
//    Sidebar: a stack of ToggleButtons along an edge.
//    SidePopup: a flyout that expands when a button toggles.
// ════════════════════════════════════════════════════════════════════════

internal static class DockSideStripRenderer
{
    /// <summary>
    /// Place the docked content in the middle with strips on whichever
    /// sides have entries. Empty sides collapse to zero width / height.
    /// </summary>
    public static Element Compose(DockManager manager, Element center)
    {
        var leftStrip = BuildVerticalStrip(manager.LeftSide);
        var rightStrip = BuildVerticalStrip(manager.RightSide);
        var topStrip = BuildHorizontalStrip(manager.TopSide);
        var bottomStrip = BuildHorizontalStrip(manager.BottomSide);

        // Middle row: [left | center | right]
        var middleRow = new FlexElement(FilterNonNull([
            leftStrip,
            center.Flex(grow: 1),
            rightStrip,
        ]))
        {
            Direction = FlexDirection.Row,
            AlignItems = FlexAlign.Stretch,
        };

        // Outer column: [top / middle / bottom]
        return new FlexElement(FilterNonNull([
            topStrip,
            middleRow.Flex(grow: 1),
            bottomStrip,
        ]))
        {
            Direction = FlexDirection.Column,
            AlignItems = FlexAlign.Stretch,
        };
    }

    private static Element[] FilterNonNull(Element?[] items)
    {
        var count = 0;
        foreach (var it in items) if (it is not null) count++;
        var result = new Element[count];
        var i = 0;
        foreach (var it in items) if (it is not null) result[i++] = it;
        return result;
    }

    private static Element? BuildVerticalStrip(IReadOnlyList<DockableContent>? items)
    {
        if (items is null or { Count: 0 }) return null;
        var buttons = new Element[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var pane = items[i];
            buttons[i] = new BorderElement(new TextBlockElement(pane.Title ?? string.Empty))
            {
                CornerRadius = 4,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
                BorderThickness = 1,
            };
        }
        return new FlexElement(buttons)
        {
            Direction = FlexDirection.Column,
            AlignItems = FlexAlign.Stretch,
            ColumnGap = 0,
            RowGap = 4,
        };
    }

    private static Element? BuildHorizontalStrip(IReadOnlyList<DockableContent>? items)
    {
        if (items is null or { Count: 0 }) return null;
        var buttons = new Element[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var pane = items[i];
            buttons[i] = new BorderElement(new TextBlockElement(pane.Title ?? string.Empty))
            {
                CornerRadius = 4,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
                BorderThickness = 1,
            };
        }
        return new FlexElement(buttons)
        {
            Direction = FlexDirection.Row,
            AlignItems = FlexAlign.Stretch,
            ColumnGap = 4,
            RowGap = 0,
        };
    }
}
