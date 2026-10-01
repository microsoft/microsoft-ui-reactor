using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Every docking tab must be identifiable, both on screen and to assistive
/// technology. Found in the Reactor IDE sample: compact tool strips rendered
/// their unselected tabs as blank ~34 DIP stubs (WinUI's Compact width mode
/// shows only the icon of an unselected tab, and docking panes have none),
/// and every pinnable tool-window tab had no UI Automation Name (its header
/// is a composite title + pin button, and WinUI derives a TabViewItem's name
/// only from a string header).
/// </summary>
/// <remarks>
/// The unit tests pin the width-mode resolution; these checks read the
/// realized WinUI tree, so they fail on the visible symptom whatever the
/// resolution logic looks like.
/// </remarks>
internal static class NativeDockingTabLabelFixtures
{
    private static ToolWindow Tool(string key, string title) =>
        new() { Title = title, Key = key, Content = TextBlock($"body:{key}") };

    private static Document Doc(string key, string title) =>
        new() { Title = title, Key = key, Content = TextBlock($"body:{key}") };

    /// <summary>
    /// The Reactor IDE sample's default layout: tool strips on the left,
    /// right and bottom around a document well. The left strip relies on the
    /// §2.8 auto-flip to compact tabs; the right and bottom strips request
    /// them explicitly.
    /// </summary>
    private static DockNode IdeLayout(ToolWindow terminal, ToolWindow errorList) =>
        new DockSplit(Orientation.Vertical, new DockNode[]
        {
            new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(
                    new DockableContent[] { Tool("tl:solution", "Solution Explorer"), Tool("tl:classes", "Class View") },
                    Width: 220,
                    Role: DockGroupRole.ToolWindowStrip),
                new DockTabGroup(
                    new DockableContent[] { Doc("tl:app", "App.cs"), Doc("tl:view", "MainView.xaml") },
                    Role: DockGroupRole.DocumentArea),
                new DockTabGroup(
                    new DockableContent[] { Tool("tl:props", "Properties"), Tool("tl:git", "Git Changes") },
                    Width: 240,
                    TabPosition: TabPosition.Bottom,
                    CompactTabs: true,
                    Role: DockGroupRole.ToolWindowStrip),
            }),
            new DockTabGroup(
                new DockableContent[] { Tool("tl:output", "Output"), terminal, errorList },
                Height: 180,
                TabPosition: TabPosition.Bottom,
                CompactTabs: true,
                Role: DockGroupRole.ToolWindowStrip),
        });

    private static List<(TabView View, TabViewItem Item)> DockTabs(Harness h) =>
        h.FindAllControls<TabView>(_ => true)
            .SelectMany(tv => tv.TabItems.OfType<TabViewItem>().Select(tvi => (tv, tvi)))
            .ToList();

    /// <summary>
    /// The title a tab displays, read from its header: a plain string, or the
    /// caption TextBlock of the composite header a pinnable tab uses.
    /// </summary>
    private static string Caption(TabViewItem tvi) => tvi.Header switch
    {
        string s => s,
        Panel p => p.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>The tab's name as UI Automation sees it; empty when it has none.</summary>
    private static string PeerName(TabViewItem tvi) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(tvi)?.GetName() ?? string.Empty;

    /// <summary>
    /// True when the tab's caption is actually on screen: a realized TextBlock
    /// with the caption's text, with a non-zero size, and with no collapsed or
    /// transparent element between it and the tab. WinUI's Compact width mode
    /// fails this for an unselected tab by collapsing the header presenter.
    /// </summary>
    private static bool CaptionRendered(TabViewItem tvi, string caption)
    {
        if (caption.Length == 0) return false;
        var text = FindDescendant<TextBlock>(tvi, tb => tb.Text == caption);
        if (text is null || text.ActualWidth <= 0 || text.ActualHeight <= 0) return false;
        for (DependencyObject? node = text; node is not null && !ReferenceEquals(node, tvi); node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement ui && (ui.Visibility != Visibility.Visible || ui.Opacity <= 0)) return false;
        }
        return true;
    }

    /// <summary>A tab is identifiable when it shows its title or carries an icon.</summary>
    private static bool Identifiable(TabViewItem tvi) =>
        CaptionRendered(tvi, Caption(tvi)) || tvi.IconSource is not null;

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) return match;
            if (FindDescendant(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    private static string Describe(IEnumerable<(TabView View, TabViewItem Item)> tabs) =>
        string.Join("; ", tabs.Select(t =>
            $"'{Caption(t.Item)}' selected={t.Item.IsSelected} width={t.Item.ActualWidth:0.#} " +
            $"mode={t.View.TabWidthMode} icon={t.Item.IconSource is not null} " +
            $"rendered={CaptionRendered(t.Item, Caption(t.Item))} name='{PeerName(t.Item)}'"));

    /// <summary>
    /// Mounts the IDE-shaped layout and asserts that (1) every unselected
    /// tool-window tab shows its title (or an icon), both at mount and after
    /// the selection moves, and (2) every docking TabViewItem carries its
    /// title as its UI Automation name — including after a re-render renames
    /// a pinnable tab and turns another from pinnable into a plain tab.
    /// </summary>
    internal class ToolTabs_LabeledAndNamed(Harness h) : SelfTestFixtureBase(h)
    {
        private const int TabCount = 9;

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var terminal = Tool("tl:terminal", "Terminal");
            var errorList = Tool("tl:errors", "Error List");
            host.Mount(_ => new DockManager { Layout = IdeLayout(terminal, errorList) });

            var realized = await Harness.WaitFor(() =>
            {
                var tabs = DockTabs(H);
                return tabs.Count == TabCount && tabs.All(t => t.Item.ActualWidth > 0);
            });
            H.Check("DockTabLabels_AllTabsRealized", realized, Describe(DockTabs(H)));
            if (!realized) { host.Mount(_ => TextBlock("dock-tab-labels-done")); await Harness.Render(); return; }
            await Harness.Render();

            var tabs = DockTabs(H);
            Console.WriteLine($"# DockTabLabels mount: {Describe(tabs)}");

            // Positive control for the next check: the layout really has
            // unselected tool-window tabs, in both an auto-flipped strip
            // (Class View) and the explicitly compact ones.
            var unselected = tabs.Where(t => !t.Item.IsSelected).ToList();
            var unselectedCaptions = unselected.Select(t => Caption(t.Item)).ToHashSet();
            H.Check("DockTabLabels_HasUnselectedToolTabs",
                new[] { "Class View", "Git Changes", "Terminal", "Error List" }.All(unselectedCaptions.Contains),
                Describe(unselected));

            var blank = unselected.Where(t => !Identifiable(t.Item)).ToList();
            H.Check("DockTabLabels_UnselectedTabsShowTitle", blank.Count == 0, Describe(blank));

            var unnamed = tabs.Where(t => Caption(t.Item).Length == 0 || PeerName(t.Item) != Caption(t.Item)).ToList();
            H.Check("DockTabLabels_EveryTabNamedFromTitle", unnamed.Count == 0, Describe(unnamed));

            // Move the bottom strip's selection the way a tab click does, so
            // the formerly selected Output tab becomes an unselected one.
            var output = tabs.First(t => Caption(t.Item) == "Output");
            var terminalTab = tabs.First(t => Caption(t.Item) == "Terminal");
            output.View.SelectedItem = terminalTab.Item;
            await Harness.WaitFor(() => terminalTab.Item.IsSelected && !output.Item.IsSelected);
            await Harness.Render();
            H.Check("DockTabLabels_SelectionMoved", terminalTab.Item.IsSelected && !output.Item.IsSelected);
            H.Check("DockTabLabels_DeselectedTabShowsTitle", Identifiable(output.Item), Describe(new[] { output }));

            // Re-render with a renamed pinnable tab (Error List → Problems)
            // and a tab that stops being pinnable while it is renamed
            // (Terminal → Console): each tab's name must follow its title
            // through the update path, not keep the name it was mounted with.
            var console = terminal with { Title = "Console", CanAutoHide = false };
            var problems = errorList with { Title = "Problems" };
            host.Mount(_ => new DockManager { Layout = IdeLayout(console, problems) });
            var renamed = await Harness.WaitFor(() =>
            {
                var captions = DockTabs(H).Select(t => Caption(t.Item)).ToList();
                return captions.Contains("Console") && captions.Contains("Problems");
            });
            await Harness.Render();
            tabs = DockTabs(H);
            Console.WriteLine($"# DockTabLabels renamed: {Describe(tabs)}");
            var consoleTab = tabs.FirstOrDefault(t => Caption(t.Item) == "Console");
            var problemsTab = tabs.FirstOrDefault(t => Caption(t.Item) == "Problems");
            Console.WriteLine(
                $"# DockTabLabels containers reused: console={ReferenceEquals(consoleTab.Item, terminalTab.Item)} " +
                $"consoleHeaderIsString={consoleTab.Item?.Header is string}");
            H.Check("DockTabLabels_RenamedTabsRendered", renamed, Describe(tabs));
            H.Check("DockTabLabels_UnpinnedRenamedTabNamedFromNewTitle",
                consoleTab.Item is not null && PeerName(consoleTab.Item) == "Console",
                Describe(tabs.Where(t => Caption(t.Item) is "Console" or "Terminal")));
            H.Check("DockTabLabels_PinnableRenamedTabNamedFromNewTitle",
                problemsTab.Item is not null && PeerName(problemsTab.Item) == "Problems",
                Describe(tabs.Where(t => Caption(t.Item) is "Problems" or "Error List")));

            unnamed = tabs.Where(t => Caption(t.Item).Length == 0 || PeerName(t.Item) != Caption(t.Item)).ToList();
            H.Check("DockTabLabels_EveryTabNamedAfterUpdate", unnamed.Count == 0, Describe(unnamed));
            blank = tabs.Where(t => !t.Item.IsSelected && !Identifiable(t.Item)).ToList();
            H.Check("DockTabLabels_UnselectedTabsShowTitleAfterUpdate", blank.Count == 0, Describe(blank));

            host.Mount(_ => TextBlock("dock-tab-labels-done"));
            await Harness.Render();
        }
    }
}
