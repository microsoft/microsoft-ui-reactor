global using Microsoft.UI.Reactor;
global using Microsoft.UI.Reactor.Core;
global using Microsoft.UI.Reactor.Layout;
global using Microsoft.UI.Xaml;
global using Microsoft.UI.Xaml.Controls;
global using DemoScriptTool.App;
global using DemoScriptTool.App.Models;
global using DemoScriptTool.App.Services;

using static Microsoft.UI.Reactor.Factories;

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
