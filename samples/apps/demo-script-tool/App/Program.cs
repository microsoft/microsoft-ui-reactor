global using Microsoft.UI.Reactor;
global using Microsoft.UI.Reactor.Core;
global using Microsoft.UI.Reactor.Layout;
global using Microsoft.UI.Xaml;
global using Microsoft.UI.Xaml.Controls;
global using DemoScriptTool.App;
global using DemoScriptTool.App.Models;
global using DemoScriptTool.App.Services;

using static Microsoft.UI.Reactor.Factories;

// Optional positional CLI argument: a folder path to load on first mount,
// skipping the manual Open Folder step. Useful for inner-loop iteration —
// e.g. `dotnet run -- C:\dev\my-demo`. Reactor's own --devtools flags pass
// straight through; we accept the first non-flag arg.
string? initialFolder = null;
foreach (var raw in System.Environment.GetCommandLineArgs().Skip(1))
{
    if (raw.StartsWith('-')) continue;
    var path = System.IO.Path.GetFullPath(raw);
    if (!System.IO.Directory.Exists(path))
    {
        System.Console.Error.WriteLine($"[demo-script-tool] '{raw}' is not a directory; ignoring.");
        continue;
    }
    initialFolder = path;
    break;
}

DemoScriptShell.InitialFolder = initialFolder;

ReactorApp.Run<DemoScriptShell>(
    "Demo Script Tool",
    width: 1280,
    height: 860,
#if DEBUG
    devtools: true,
#endif
    configure: host =>
    {
        // Spec §High Contrast — opt out of OS HC color injection so theme
        // tokens stay authoritative.
        Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(host.Window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "demo-script-tool.ico");
        if (System.IO.File.Exists(iconPath))
            appWindow.SetIcon(iconPath);
    });
