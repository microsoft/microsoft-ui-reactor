using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class TabbedCommandBarChecks
{
    internal static void Configure(Wct.TabbedCommandBar bar, int phase)
    {
        bar.Width = 500;
        bar.Height = 250;
        if (bar.MenuItems.Count == 0)
        {
            foreach (var header in new[] { "Home", "View" })
            {
                var tab = new Wct.TabbedCommandBarItem { Header = header };
                tab.PrimaryCommands.Add(new Native.AppBarButton { Label = "initial-label" });
                bar.MenuItems.Add(tab);
            }
        }
        Program.Check(bar.MenuItems.Count == 2, "Expected two native tabs.");
        foreach (var tab in bar.MenuItems.Cast<Wct.TabbedCommandBarItem>())
            ((Native.AppBarButton)tab.PrimaryCommands[0]).Label = phase == 0 ? "initial-label" : "updated-label";
        bar.SelectedItem = bar.MenuItems[phase];
    }

    internal static void Check(FrameworkElement element, int phase)
    {
        var bar = (Wct.TabbedCommandBar)element;
        Program.Check(bar.MenuItems.Count == 2 && ReferenceEquals(bar.SelectedItem, bar.MenuItems[phase]),
            "The native command bar did not select the expected tab.");
        var tab = (Wct.TabbedCommandBarItem)bar.MenuItems[phase];
        var button = (Native.AppBarButton)tab.PrimaryCommands[0];
        string label = phase == 0 ? "initial-label" : "updated-label";
        Program.Check(button.Label == label && button.IsLoaded && button.ActualWidth > 0,
            $"Expected the realized native command '{label}'.");
        var presenter = CompatibilityRoot.Descendants(bar).OfType<Native.ContentControl>()
            .Single(c => c.Name == "PART_TabbedCommandBarContent");
        Program.Check(ReferenceEquals(presenter.Content, tab), "Native selected-tab presenter has the wrong content.");
        Program.Check(FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetName() == label,
            "Native command accessible name did not update.");
    }

    internal static async Task SampleAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("tcb"));
        var bar = Find();
        Program.Check(bar.MenuItems.Count == 2, "Actual sample did not create its two native tabs.");
        var tabs = bar.MenuItems.Cast<Wct.TabbedCommandBarItem>().ToArray();
        Program.Check(Equals(tabs[0].Header, "Home") && Equals(tabs[1].Header, "View") &&
            tabs[0].PrimaryCommands.Count == 4 && tabs[1].PrimaryCommands.Count == 2,
            "Actual sample tab/command inventory differs from Home(4)/View(2).");
        int clicks = 0;
        for (int i = 0; i < tabs.Length; i++)
        {
            bar.SelectedItem = tabs[i];
            var button = (Native.AppBarButton)tabs[i].PrimaryCommands[0];
            await CompatibilityRoot.WaitAsync(() => button.IsLoaded, "Selected tab command realized");
            void Clicked(object sender, RoutedEventArgs args) => clicks++;
            button.Click += Clicked;
            try
            {
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                Program.Check(peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider,
                    "Native command has no Invoke provider.");
                ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke();
                int expected = i + 1;
                await CompatibilityRoot.WaitAsync(() => clicks == expected, "Native command Click callback");
                Program.Results.Note($"sample tab={tabs[i].Header}; command={button.Label}; native clicks={clicks}; public UIA Invoke");
            }
            finally { button.Click -= Clicked; }
        }
        await root.ShowAsync(TextBlock("Command bar removed"));
        Program.Check(!bar.IsLoaded, "Command bar remained loaded after removal.");
        await root.ShowAsync(WctControls.SamplePages.CreatePage("tcb"));
        var fresh = Find();
        Program.Check(!ReferenceEquals(bar, fresh) && fresh.MenuItems.Count == 2 &&
            !ReferenceEquals(tabs[0], fresh.MenuItems[0]), "Remount reused old native tabs.");
    }

    private static Wct.TabbedCommandBar Find() =>
        CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<Wct.TabbedCommandBar>().Single();
}
