using Microsoft.UI.Xaml;
using StressPerf.Shared;

namespace StressPerf.XBind;

public partial class App : Application
{
    public static CliOptions Options { get; set; } = new();

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(Options);
        window.Activate();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        Options = CliOptions.Parse(args);
        if (Options.Headless)
            ConsoleHelper.EnsureConsole();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
