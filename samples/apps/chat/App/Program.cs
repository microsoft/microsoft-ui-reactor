global using Microsoft.UI.Reactor;
global using Microsoft.UI.Reactor.Core;
global using Microsoft.UI.Xaml;
global using Microsoft.UI.Xaml.Media;
global using Microsoft.UI.Xaml.Controls;
global using ChatSample.App;
global using ChatSample.Chat.Model;
global using ChatSample.Chat.UI;

ReactorApp.Run<ChatSampleApp>("Chat Sample", width: 1200, height: 800
    , configure: host =>
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(host.Window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "chat.ico");
        if (System.IO.File.Exists(iconPath))
            appWindow.SetIcon(iconPath);

        Notifications.Initialize();

        host.Window.Activated += (_, args) =>
        {
            Notifications.IsWindowFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        };
    }
);
