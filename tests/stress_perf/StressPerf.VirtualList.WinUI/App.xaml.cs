using Microsoft.UI.Xaml;
using StressPerf.Shared;

namespace StressPerf.VirtualList.WinUI;

public sealed partial class App : Application
{
    public static VirtualListCli Cli { get; set; } = new();

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(Cli);
        window.Activate();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        Cli = VirtualListCli.Parse(args);
        if (Cli.Headless)
            ConsoleHelper.EnsureConsole();

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
