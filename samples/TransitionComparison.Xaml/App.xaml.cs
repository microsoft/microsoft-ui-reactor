using Microsoft.UI.Xaml;

namespace TransitionComparison.Xaml;

/// <summary>
/// Vanilla WinUI 3 entry point — the control side of the transition comparison.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
