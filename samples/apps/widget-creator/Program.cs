global using Microsoft.UI.Reactor;
global using Microsoft.UI.Reactor.Core;
global using Microsoft.UI.Reactor.Layout;
global using Microsoft.UI.Xaml;
global using WidgetCreator;
global using WidgetCreator.Services;

using static Microsoft.UI.Reactor.Factories;

// Widget Creator — an app that creates apps.
//
// Type a prompt, the GitHub Copilot SDK generates a single-file Reactor app,
// we build it, and launch the result inside an MXC sandbox with UI + remote
// network but NO local filesystem access (a web-like experience).

SessionLog.Init();
SessionLog.Write($"[Program] launched args={string.Join(' ', System.Environment.GetCommandLineArgs())}");

// Headless diagnostic: report whether the machine-wide Windows App Runtime that
// generated widgets bind at launch is installed, then exit. Useful from a
// terminal or CI without opening the window, and it exercises exactly the same
// probe the in-app banner uses.
//   exit 0 = satisfied, 1 = missing/outdated.
if (System.Array.Exists(System.Environment.GetCommandLineArgs(),
        a => string.Equals(a, "--check-runtime", System.StringComparison.OrdinalIgnoreCase)))
{
    var info = WindowsAppRuntimeCheck.Detect();
    System.Console.WriteLine($"status    : {info.Status}");
    System.Console.WriteLine($"package   : {info.PackageFamilyName} ({info.Architecture})");
    System.Console.WriteLine($"required  : {info.RequiredVersion}");
    System.Console.WriteLine($"installed : {info.InstalledVersion?.ToString() ?? "(none)"}");
    System.Console.WriteLine($"message   : {info.Message}");
    if (!info.IsSatisfied)
    {
        System.Console.WriteLine($"installer : {info.InstallerUrl}");
        System.Console.WriteLine($"downloads : {info.DownloadsUrl}");
        System.Console.WriteLine($"winget    : {info.WingetCommand}");
    }
    return info.IsSatisfied ? 0 : 1;
}

ReactorApp.Run<WidgetCreatorShell>(
    "Widget Creator",
    width: 1180,
    height: 820,
    configure: host => Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(host.Reconciler));

return 0;
