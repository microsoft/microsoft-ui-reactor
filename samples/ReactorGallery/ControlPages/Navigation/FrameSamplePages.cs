using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinUIGalleryReactor.ControlPages.Navigation.Pages;

// Phase 8.3 — trivial Page-derived navigation targets for the Frame
// micro-sample. Authored in code (no XAML) to keep the sample compact.
// The host gallery is a Reactor app, but FrameElement bridges to WinUI
// by calling Frame.Navigate(Type), which requires real Page subclasses.

internal sealed partial class FrameSampleHomePage : Page
{
    public FrameSampleHomePage()
    {
        Content = new TextBlock
        {
            Text = "Home page",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Background = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x80, 0xFF));
    }
}

internal sealed partial class FrameSampleDetailsPage : Page
{
    public FrameSampleDetailsPage()
    {
        Content = new TextBlock
        {
            Text = "Details page",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Background = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x00, 0xFF));
    }
}

/// <summary>
/// Throws from its constructor so Frame.Navigate raises NavigationFailed
/// when targeting it.
/// </summary>
internal sealed partial class FrameSampleBrokenPage : Page
{
    public FrameSampleBrokenPage()
    {
        throw new global::System.InvalidOperationException(
            "FrameSampleBrokenPage intentionally throws to demonstrate NavigationFailed.");
    }
}
