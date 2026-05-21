using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.5 — side strips + side popup.
//
//  Translates WinUI.Dock's Sidebar / SidePopup pattern:
//    • Strip: thin column / row of buttons, one per pinned pane.
//    • Popup: light-dismiss WinUI Popup anchored to the strip's edge,
//      containing the active pane's Content. Click outside → close.
//
//  P2 first cut intentionally defers:
//    • Sizer (resize the popup) — lands when a Reactor Splitter primitive
//      can live inside a Popup. The popup's default size matches the
//      upstream third-of-host rule (Manager.PopupContainer.ActualX / 3).
//    • Pin button (from popup → re-dock at PreferredSide) — pairs with
//      the §2.4 drag/pin gesture.
//    • Close-from-popup affordance — pairs with §2.10 keyboard chords.
// ════════════════════════════════════════════════════════════════════════

internal static class DockSideStripRenderer
{
    /// <summary>
    /// Place the docked content in the middle with strips on whichever
    /// sides have entries. Renders one shared <see cref="PopupElement"/>
    /// at the manager root whose <c>IsOpen</c> tracks <paramref name="expandedPaneKey"/>.
    /// </summary>
    /// <remarks>
    /// The four <c>effective*Side</c> arguments are the host's resolved
    /// side lists — the §2.16 drain feeds in a per-host override when
    /// programmatic <c>Hide</c> / <c>PinToSide</c> mutations have moved
    /// panes between the docked tree and a side strip; when no override
    /// is in flight the caller passes the controlled element's lists
    /// unchanged.
    /// </remarks>
    public static Element Compose(
        DockManager manager,
        Element center,
        IReadOnlyList<DockableContent>? effectiveLeftSide,
        IReadOnlyList<DockableContent>? effectiveTopSide,
        IReadOnlyList<DockableContent>? effectiveRightSide,
        IReadOnlyList<DockableContent>? effectiveBottomSide,
        object? expandedPaneKey,
        Action<object?> setExpandedPaneKey)
    {
        _ = manager; // reserved for future renderer choices keyed on chrome
        _ = setExpandedPaneKey; // reserved for future light-dismiss wiring
        var allSides = new (IReadOnlyList<DockableContent>? items, DockSide side)[]
        {
            (effectiveLeftSide,   DockSide.Left),
            (effectiveTopSide,    DockSide.Top),
            (effectiveRightSide,  DockSide.Right),
            (effectiveBottomSide, DockSide.Bottom),
        };

        DockableContent? expanded = null;
        DockSide expandedSide = DockSide.Left;
        foreach (var (items, side) in allSides)
        {
            if (items is null) continue;
            foreach (var pane in items)
            {
                if (expandedPaneKey is null) break;
                if (Equals(pane.Key, expandedPaneKey))
                {
                    expanded = pane;
                    expandedSide = side;
                }
            }
            if (expanded is not null) break;
        }

        var leftStrip = BuildVerticalStrip(effectiveLeftSide, DockSide.Left, expandedPaneKey, setExpandedPaneKey);
        var rightStrip = BuildVerticalStrip(effectiveRightSide, DockSide.Right, expandedPaneKey, setExpandedPaneKey);
        var topStrip = BuildHorizontalStrip(effectiveTopSide, DockSide.Top, expandedPaneKey, setExpandedPaneKey);
        var bottomStrip = BuildHorizontalStrip(effectiveBottomSide, DockSide.Bottom, expandedPaneKey, setExpandedPaneKey);

        // Middle row: [left | center | right]
        var middleRow = new FlexElement(FilterNonNull(new Element?[]
        {
            leftStrip,
            center.Flex(grow: 1),
            rightStrip,
        }))
        {
            Direction = FlexDirection.Row,
            AlignItems = FlexAlign.Stretch,
        };

        // Outer column: [top / middle / bottom]
        var outerStack = new FlexElement(FilterNonNull(new Element?[]
        {
            topStrip,
            middleRow.Flex(grow: 1),
            bottomStrip,
        }))
        {
            Direction = FlexDirection.Column,
            AlignItems = FlexAlign.Stretch,
        };

        // Only mount the Popup when a pane is expanded — letting the
        // Popup unmount on collapse keeps the WinUI Popup's IsOpen state
        // synced with our model. Always-mount + toggle IsOpen ran into
        // WinUI's "Popup ignores IsOpen=true while detached" semantic;
        // upstream WinUI.Dock's SidePopup is itself a flyout-per-expand
        // for the same reason.
        if (expanded is null) return outerStack;

        var popup = BuildSidePopup(pane: expanded, side: expandedSide);

        // Use a Grid to overlay the popup on the outer stack. The Popup
        // sits in the visual tree but doesn't take layout space (its
        // child is hosted in a separate PopupRoot).
        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Star(1) },
            outerStack.Grid(row: 0, column: 0),
            popup.Grid(row: 0, column: 0));
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

    private static Element? BuildVerticalStrip(
        IReadOnlyList<DockableContent>? items,
        DockSide side,
        object? expandedPaneKey,
        Action<object?> setExpandedPaneKey)
    {
        if (items is null or { Count: 0 }) return null;
        var buttons = new Element[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var pane = items[i];
            var isExpanded = expandedPaneKey is not null && Equals(pane.Key, expandedPaneKey);
            buttons[i] = SidePinButton(pane, side, isExpanded, setExpandedPaneKey);
        }
        return new FlexElement(buttons)
        {
            Direction = FlexDirection.Column,
            AlignItems = FlexAlign.Stretch,
            RowGap = 4,
        };
    }

    private static Element? BuildHorizontalStrip(
        IReadOnlyList<DockableContent>? items,
        DockSide side,
        object? expandedPaneKey,
        Action<object?> setExpandedPaneKey)
    {
        if (items is null or { Count: 0 }) return null;
        var buttons = new Element[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var pane = items[i];
            var isExpanded = expandedPaneKey is not null && Equals(pane.Key, expandedPaneKey);
            buttons[i] = SidePinButton(pane, side, isExpanded, setExpandedPaneKey);
        }
        return new FlexElement(buttons)
        {
            Direction = FlexDirection.Row,
            AlignItems = FlexAlign.Stretch,
            ColumnGap = 4,
        };
    }

    private static Element SidePinButton(
        DockableContent pane,
        DockSide side,
        bool isExpanded,
        Action<object?> setExpandedPaneKey)
    {
        var title = pane.Title ?? string.Empty;
        var background = isExpanded
            ? new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80))
            : new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80));
        return Button(title, () => setExpandedPaneKey(isExpanded ? null : pane.Key))
            .Background(background)
            .Padding(8, 4)
            .CornerRadius(4)
            .ToolTip(DockingStrings.SidePinTooltip(title));
    }

    private static Element BuildSidePopup(DockableContent? pane, DockSide side)
    {
        // When no pane is expanded, render an empty Popup with IsOpen=false
        // so the WinUI control persists across renders (no remount churn).
        if (pane is null)
        {
            return new PopupElement(new BorderElement(null))
            {
                IsOpen = false,
                IsLightDismissEnabled = false,
            };
        }

        var content = pane.Content ?? (Element)new BorderElement(null);
        // Wrap content with the same pane-context envelope used by the
        // docked tree so hooks (UsePane, UseDockState) resolve inside an
        // auto-hidden popup too. PaneState reflects the AutoHiddenExpanded
        // transition.
        var info = new DockPaneInfo(pane.Key, pane.Title ?? string.Empty, pane);
        var paneSubtree = content
            .Padding(16)
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.AutoHiddenExpanded);

        // Default size: WinUI.Dock's "host / 3" rule, with width-vs-height
        // chosen by side orientation. We can't read the host's ActualWidth
        // at element-tree construction time; use a reasonable default and
        // let the user resize via the host frame later (§2.5 sizer item).
        const double Default = 320;
        var box = new BorderElement(paneSubtree)
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x50, 0x50, 0x50)),
            BorderThickness = 1,
            CornerRadius = 4,
        }.Width(side is DockSide.Top or DockSide.Bottom ? 600 : Default)
         .Height(side is DockSide.Top or DockSide.Bottom ? Default : 480);

        // For Phase 2 the popup dismisses via repeat-click on the side
        // button. WinUI's own Closed event is observed but ignored — in
        // some hosting paths (notably the headless self-test harness)
        // WinUI fires Closed synchronously on a freshly-opened Popup when
        // no focus owner exists, which would immediately roll back the
        // expanded state. Light-dismiss + focus arbitration land with the
        // §2.4 drag pipeline once the host supplies focus anchoring.
        // Defer IsOpen=true until after the Popup is loaded into the
        // visual tree. WinUI silently ignores IsOpen=true on a detached
        // Popup (no XamlRoot yet), so the property is set inside a
        // Loaded handler attached via Setters. This is the same pattern
        // upstream WinUI.Dock's SidePopup uses — its `Show()` method runs
        // after the popup has been attached to its container.
        return new PopupElement(box)
        {
            IsOpen = false,
            IsLightDismissEnabled = false,
            Setters = new Action<Microsoft.UI.Xaml.Controls.Primitives.Popup>[]
            {
                static p =>
                {
                    if (p.IsLoaded) { p.IsOpen = true; return; }
                    Microsoft.UI.Xaml.RoutedEventHandler? handler = null;
                    handler = (_, _) =>
                    {
                        if (handler is not null) p.Loaded -= handler;
                        p.IsOpen = true;
                    };
                    p.Loaded += handler;
                }
            },
        };
    }
}
