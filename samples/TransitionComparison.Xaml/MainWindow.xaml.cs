using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace TransitionComparison.Xaml;

/// <summary>
/// Drives a real WinUI <see cref="Microsoft.UI.Xaml.Controls.Frame"/> so each button plays the
/// stock platform motion. The Reactor twin replays these on the Composition layer; putting the
/// two windows side by side is the only honest way to check how close that replay is.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Alternates so every navigation is a real page change.
    private int _next;

    public MainWindow()
    {
        InitializeComponent();

        // Match the Reactor twin's window so the two can sit side by side without one of them
        // rescaling the stage — transition distances are in pixels, so size matters here.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1040, 800));

        // Seed the first page without animating — the Reactor twin starts on Page A too.
        Stage.Navigate(typeof(PageA), null, new SuppressNavigationTransitionInfo());
        _next = 1;
    }

    private void Navigate(string label, NavigationTransitionInfo? info)
    {
        PlayingLabel.Text = $"Playing: {label}";

        var target = _next == 0 ? typeof(PageA) : typeof(PageB);
        _next = _next == 0 ? 1 : 0;

        // Passing null lets the Frame use its own default, which is what "Default" means here.
        if (info is null)
            Stage.Navigate(target);
        else
            Stage.Navigate(target, null, info);

        BackButton.IsEnabled = Stage.CanGoBack;
    }

    private void OnDefault(object sender, RoutedEventArgs e) => Navigate("Default", null);

    private void OnEntrance(object sender, RoutedEventArgs e) =>
        Navigate("Entrance", new EntranceNavigationTransitionInfo());

    private void OnSlideFromRight(object sender, RoutedEventArgs e) =>
        Navigate("Slide — FromRight", new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromRight,
        });

    private void OnSlideFromLeft(object sender, RoutedEventArgs e) =>
        Navigate("Slide — FromLeft", new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromLeft,
        });

    private void OnSlideFromBottom(object sender, RoutedEventArgs e) =>
        Navigate("Slide — FromBottom", new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromBottom,
        });

    private void OnDrillIn(object sender, RoutedEventArgs e) =>
        Navigate("DrillIn", new DrillInNavigationTransitionInfo());

    private void OnSuppress(object sender, RoutedEventArgs e) =>
        Navigate("None", new SuppressNavigationTransitionInfo());

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (!Stage.CanGoBack) return;

        Stage.GoBack();
        _next = _next == 0 ? 1 : 0;
        BackButton.IsEnabled = Stage.CanGoBack;
    }
}
