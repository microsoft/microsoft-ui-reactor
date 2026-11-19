using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class TabbedCommandBarSampleChecks
{
    internal static async Task SampleAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("tcb"));
        var bar = FindBar();
        CheckDocument(bar);
        CheckStatus(0, "None");
        var tabs = bar.MenuItems.Cast<Wct.TabbedCommandBarItem>().ToArray();
        Program.Check(tabs.Length == 2 && Equals(tabs[0].Header, "Home") &&
            Equals(tabs[1].Header, "View") &&
            tabs[0].PrimaryCommands.Count == 4 && tabs[1].PrimaryCommands.Count == 2,
            "Expected actual Toolkit Home(4)/View(2) tabs and commands.");
        Program.Check(ReferenceEquals(bar.SelectedItem, tabs[0]), "Home must initially be selected.");
        var buttons = tabs.SelectMany(t => t.PrimaryCommands).OfType<Native.AppBarButton>().ToArray();
        Program.Check(buttons.Select(b => b.Label).SequenceEqual(["Add", "Edit", "Share", "Zoom", "Refresh"]),
            "Unexpected native sample command labels.");
        await InvokeBurstAsync(buttons[0], buttons[1]);
        try
        {
            await CompatibilityRoot.WaitAsync(() => Status.Text == StatusText(2, "Edit"),
                "Batched command feedback after two native Click events");
        }
        catch (TimeoutException error)
        {
            throw new InvalidOperationException(
                $"Expected '{StatusText(2, "Edit")}' after the burst, actual '{Status.Text}'.", error);
        }
        CheckStatus(2, "Edit");
        int count = 2;
        foreach (var tab in tabs)
        {
            bar.SelectedItem = tab;
            foreach (var button in tab.PrimaryCommands.OfType<Native.AppBarButton>())
            {
                // Invoke each command twice, across real component rerenders, to expose stale closures.
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    await InvokeAsync(button);
                    int expected = ++count;
                    await CompatibilityRoot.WaitAsync(() => Status.Text == StatusText(expected, button.Label),
                        $"Visible command feedback {expected}/{button.Label}");
                    CheckStatus(expected, button.Label);
                    Program.Check(ReferenceEquals(bar, FindBar()) &&
                        bar.MenuItems.Cast<Wct.TabbedCommandBarItem>().SequenceEqual(tabs) &&
                        tabs.SelectMany(t => t.PrimaryCommands).OfType<Native.AppBarButton>().SequenceEqual(buttons) &&
                        ReferenceEquals(bar.SelectedItem, tab),
                        "A command rerender replaced native tabs/commands or reset selection.");
                    CheckDocument(bar);
                }
            }
        }
        CheckStatus(12, "Refresh");
        CheckDocument(bar);
        Program.Results.Note("actual sample mount/update: external document, two-command burst and all five commands twice; native objects retained");

        var page = CompatibilityRoot.Descendants(WindowRoot).OfType<FrameworkElement>()
            .Single(e => AutomationProperties.GetAutomationId(e) == "Page.TabbedCommandBar");
        var originalTheme = page.RequestedTheme;
        try
        {
            page.RequestedTheme = ElementTheme.Light;
            await CompatibilityRoot.WaitAsync(() => Document.ActualTheme == ElementTheme.Light, "Light document");
            await Task.Delay(100);
            var light = ((SolidColorBrush)Document.Background).Color;
            page.RequestedTheme = ElementTheme.Dark;
            await CompatibilityRoot.WaitAsync(() => Document.ActualTheme == ElementTheme.Dark, "Dark document");
            await Task.Delay(100);
            var dark = ((SolidColorBrush)Document.Background).Color;
            Program.Check(light != dark, $"Document background did not respond to theme: light={light}, dark={dark}.");
            CheckDocument(bar);
            Program.Results.Note($"actual sample theme backgrounds: light={light}; dark={dark}");
        }
        finally { page.RequestedTheme = originalTheme; }

        await root.ShowAsync(TextBlock("Command bar removed"));
        Program.Check(!bar.IsLoaded && buttons.All(b => !b.IsLoaded), "Old native commands remained loaded.");
        await root.ShowAsync(WctControls.SamplePages.CreatePage("tcb"));
        var fresh = FindBar();
        Program.Check(!ReferenceEquals(bar, fresh) && fresh.MenuItems.Count == 2 &&
            !ReferenceEquals(tabs[0], fresh.MenuItems[0]), "Remount reused old native tabs.");
        CheckDocument(fresh);
        CheckStatus(0, "None");
        var freshButton = (Native.AppBarButton)((Wct.TabbedCommandBarItem)fresh.MenuItems[0]).PrimaryCommands[0];
        await InvokeAsync(freshButton);
        await CompatibilityRoot.WaitAsync(() => Status.Text == StatusText(1, "Add"), "Fresh command callback");
        CheckStatus(1, "Add");
        Program.Results.Note("actual sample remove/remount: detached old controls, fresh tabs, feedback reset and new command delivered");
    }

    private static UIElement WindowRoot => ReactorApp.PrimaryWindow!.NativeWindow.Content;
    private static Wct.TabbedCommandBar FindBar() =>
        CompatibilityRoot.Descendants(WindowRoot).OfType<Wct.TabbedCommandBar>().Single();
    private static Native.Border Document => CompatibilityRoot.Descendants(WindowRoot).OfType<Native.Border>()
        .Single(e => AutomationProperties.GetAutomationId(e) == "TabbedCommandBar.Document");
    private static Native.TextBlock Status => CompatibilityRoot.Descendants(WindowRoot).OfType<Native.TextBlock>()
        .Single(e => AutomationProperties.GetAutomationId(e) == "TabbedCommandBar.CommandStatus");
    private static string StatusText(int count, string command) => $"Commands invoked: {count}. Last command: {command}.";

    private static void CheckStatus(int count, string command)
    {
        Program.Check(Status.Text == StatusText(count, command),
            $"Expected '{StatusText(count, command)}', actual '{Status.Text}'.");
        CheckVisible(Status);
    }

    private static void CheckDocument(Wct.TabbedCommandBar bar)
    {
        var body = CompatibilityRoot.Descendants(WindowRoot).OfType<Native.TextBlock>()
            .SingleOrDefault(t => t.Text.StartsWith("Document body", StringComparison.Ordinal));
        Program.Check(body is not null, "Actual sample document body is absent from the visual tree.");
        CheckVisible(body);
        Program.Check(!CompatibilityRoot.Descendants(bar).Contains(body),
            "Document must be composed outside the native command bar.");
        var documentBounds = body.TransformToVisual(WindowRoot)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, body.ActualWidth, body.ActualHeight));
        var barBounds = bar.TransformToVisual(WindowRoot)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, bar.ActualWidth, bar.ActualHeight));
        Program.Check(documentBounds.Top >= barBounds.Bottom, "Document must appear below the ribbon.");
    }

    private static void CheckVisible(FrameworkElement element)
    {
        var bounds = element.TransformToVisual(WindowRoot)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var window = (FrameworkElement)WindowRoot;
        Program.Check(element.IsLoaded && bounds.Width > 0 && bounds.Height > 0 &&
            bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ActualWidth &&
            bounds.Bottom <= window.ActualHeight, $"Not visibly arranged in the window: {bounds}.");
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement visual)
                Program.Check(visual.Visibility == Visibility.Visible && visual.Opacity > 0,
                    "Document/command feedback has an invisible ancestor.");
    }

    private static async Task InvokeAsync(Native.AppBarButton button)
    {
        await CompatibilityRoot.WaitAsync(() => button.IsLoaded && button.ActualWidth > 0,
            $"Native command {button.Label} realized");
        int clicks = 0;
        void Clicked(object sender, RoutedEventArgs args) => clicks++;
        button.Click += Clicked;
        try
        {
            InvokeProvider(button).Invoke();
            await CompatibilityRoot.WaitAsync(() => clicks == 1, "Native Click after public UIA Invoke");
            Program.Results.Note($"native command={button.Label}; public UIA Invoke; Click count={clicks}; not physical input");
        }
        finally { button.Click -= Clicked; }
    }

    private static async Task InvokeBurstAsync(Native.AppBarButton first, Native.AppBarButton second)
    {
        await CompatibilityRoot.WaitAsync(() => first.IsLoaded && first.ActualWidth > 0 &&
            second.IsLoaded && second.ActualWidth > 0, "Both native burst commands realized");
        var firstProvider = InvokeProvider(first);
        var secondProvider = InvokeProvider(second);
        int firstClicks = 0, secondClicks = 0;
        void FirstClicked(object sender, RoutedEventArgs args) => firstClicks++;
        void SecondClicked(object sender, RoutedEventArgs args) => secondClicks++;
        first.Click += FirstClicked;
        second.Click += SecondClicked;
        try
        {
            firstProvider.Invoke();
            secondProvider.Invoke();
            await CompatibilityRoot.WaitAsync(() => firstClicks > 0 && secondClicks > 0,
                "Both native Click events after consecutive UIA invokes");
            Program.Check(firstClicks == 1 && secondClicks == 1,
                $"Expected one Click per burst command, got {first.Label}={firstClicks}, {second.Label}={secondClicks}.");
            Program.Results.Note($"native burst={first.Label},{second.Label}; Click count={firstClicks + secondClicks}; no yield between public UIA invokes; not physical input");
        }
        finally
        {
            first.Click -= FirstClicked;
            second.Click -= SecondClicked;
        }
    }

    private static IInvokeProvider InvokeProvider(Native.AppBarButton button)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        Program.Check(peer?.GetName() == button.Label, "Native command accessible name differs from label.");
        return peer?.GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Missing native Invoke provider.");
    }
}
