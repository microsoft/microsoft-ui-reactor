using Microsoft.UI.Xaml;

namespace InteropFirst;

/// <summary>
/// Vanilla WinUI 3 application entry. Spec 033 §7 — Reactor is the *guest*
/// here, not the host. <see cref="MainWindow"/> drives navigation and content.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
