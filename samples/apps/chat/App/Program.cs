global using Microsoft.UI.Reactor;
global using Microsoft.UI.Reactor.Core;
global using Microsoft.UI.Xaml;
global using Microsoft.UI.Xaml.Media;
global using Microsoft.UI.Xaml.Controls;
global using ChatSample.App;
global using ChatSample.Chat.Model;
global using ChatSample.Chat.UI;

ReactorApp.Run<ChatSampleApp>("Chat Sample", width: 1200, height: 800
    , icon: WindowIcon.FromPath("Assets/chat.ico")
    , configure: host =>
    {
        Notifications.Initialize();

        host.Window.Activated += (_, args) =>
        {
            Notifications.IsWindowFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        };
    }
);
