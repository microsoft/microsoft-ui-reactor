using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ContentDialogThemeSecondaryWindowFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Title = "Secondary dialog theme";

    public override async Task RunAsync()
    {
        var appTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark : ElementTheme.Light;
        var opposite = appTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        var primaryHost = H.CreateHost();
        primaryHost.Mount(_ => VStack(TextBlock("Primary window")).RequestedTheme(appTheme));
        var harnessRoot = (FrameworkElement)H.Window.Content;
        var originalTheme = harnessRoot.ReadLocalValue(FrameworkElement.RequestedThemeProperty);
        var secondary = new Window { Title = "Secondary theme owner" };
        var host = new ReactorHost(secondary);
        try
        {
            host.Mount(_ => VStack(ContentDialog(Title, TextBlock("Secondary content"), "OK")
                with { IsOpen = true }).RequestedTheme(opposite));
            secondary.Activate();
            // A newly allocated XamlRoot may exist before its visual tree is
            // connected. GetOpenPopupsForXamlRoot rejects that intermediate root.
            if (!await Harness.WaitFor(() => secondary.Content is FrameworkElement { IsLoaded: true }
                && secondary.Content.XamlRoot?.IsHostVisible == true))
                throw new InvalidOperationException("Secondary window did not finish loading.");
            await Harness.WaitFor(() => Find(secondary.Content.XamlRoot) is not null);
            var dialog = Find(secondary.Content.XamlRoot)
                ?? throw new InvalidOperationException("Secondary dialog did not open in its own XamlRoot.");
            await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, "Secondary content") is not null);
            H.Check("DialogThemeSecondary_UsesOwningWindow", dialog.XamlRoot == secondary.Content.XamlRoot
                && dialog.XamlRoot != H.Window.Content.XamlRoot
                && ContentDialogProbe.FindOpen(H, Title) is null);
            CheckTheme("Initial", opposite, dialog);
            ((FrameworkElement)secondary.Content).RequestedTheme = appTheme;
            await Harness.WaitFor(() => Find(secondary.Content.XamlRoot)?.ActualTheme == appTheme);
            CheckTheme("FollowsOnlyItsOwnOwner", appTheme, dialog);

            harnessRoot.RequestedTheme = opposite;
            await Harness.Render();
            CheckTheme("OtherWindowDoesNotOverride", appTheme, dialog);
        }
        finally
        {
            host.Dispose();
            secondary.Close();
            // The harness root outlives this fixture; restore its own declaration.
            if (ReferenceEquals(originalTheme, DependencyProperty.UnsetValue))
                harnessRoot.ClearValue(FrameworkElement.RequestedThemeProperty);
            else
                harnessRoot.SetValue(FrameworkElement.RequestedThemeProperty, originalTheme);
            await ContentDialogProbe.WaitForNoneOpen(H);
        }

        void CheckTheme(string name, ElementTheme expected, WinUI.ContentDialog original)
        {
            var current = Find(secondary.Content.XamlRoot);
            H.Check("DialogThemeSecondary_" + name, ReferenceEquals(current, original)
                && current?.RequestedTheme == expected && current.ActualTheme == expected
                && ContentDialogProbe.FindText(current, "Secondary content")?.ActualTheme == expected,
                $"App={appTheme}; expected={expected}; actual={current?.ActualTheme}");
        }
    }

    private static WinUI.ContentDialog? Find(XamlRoot? root)
    {
        if (root is null) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is not null && ContentDialogProbe.Walk<WinUI.ContentDialog>(
                popup.Child, dialog => dialog.Title as string == Title) is { } dialog)
                return dialog;
        }
        return null;
    }
}
