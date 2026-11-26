using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;

// <snippet:activation>
ReactorApp.Run(ctx =>
{
    var launch = ctx.LaunchActivation;

    // Arguments and Files come from outside the app. Validate them before use.
    string status = launch.Kind switch
    {
        LaunchKind.File => $"Opened with {launch.Files.Count} file(s)",
        LaunchKind.Protocol => $"Opened from a link: {launch.Arguments}",
        LaunchKind.Toast => "Opened from a notification",
        LaunchKind.JumpList => $"Opened from the taskbar: {launch.Arguments}",
        _ => "Normal launch",
    };

    ReactorApp.OpenWindow(
        new WindowSpec { Title = "Desktop integration", Width = 600, Height = 520 },
        _ => Component<DesktopShell, string>(status));
});
// </snippet:activation>

// <snippet:close-to-tray>
class DesktopShell : Component<string>
{
    public override Element Render()
    {
        var window = UseWindow();

        UseEffect(() =>
        {
            if (window is null) return () => { };

            // Only a user close (caption button, Alt+F4) becomes a hide.
            // A close from app code (Reason == AppClosed) goes through.
            void onClosing(object? s, WindowClosingEventArgs e)
            {
                if (e.Reason != WindowCloseReason.UserClosed) return;
                e.Cancel = true;
                window.Hide();
            }

            window.Closing += onClosing;
            return () => window.Closing -= onClosing;
        }, window ?? (object)"no-window");

        return VStack(16,
            TextBlock(Props),
            Component<TrayMenuHost>(),
            Component<ClipboardDemo>()
        ).Padding(24);
    }
}
// </snippet:close-to-tray>

// <snippet:tray-menu>
class TrayMenuHost : Component
{
    public override Element Render()
    {
        var icon = UseMemo(() => WindowIcon.FromPath("Assets/TrayIcon.ico"));
        var tray = UseTrayIcon(new TrayIconSpec(
            Icon: icon,
            Tooltip: "Contoso Sync",
            Key: WindowKey.Of("sync-tray")));

        UseEffect(() =>
        {
            if (tray is null) return () => { };

            void onClick(object? s, EventArgs e) => BringBack();

            // The flyout is a Reactor element. It opens at the cursor and
            // closes when it loses focus, for example on a click outside it.
            void onRightClick(object? s, EventArgs e) => tray.ShowFlyout(
                VStack(4,
                    Button("Open Contoso Sync", () =>
                    {
                        tray.HideFlyout();
                        BringBack();
                    }),
                    Button("Quit", () => ReactorApp.Exit())
                ).Padding(8));

            tray.Click += onClick;
            tray.RightClick += onRightClick;
            return () =>
            {
                tray.Click -= onClick;
                tray.RightClick -= onRightClick;
            };
        }, tray ?? (object)"no-tray");

        return TextBlock("Right-click the tray icon to open its menu.");
    }

    // Show() undoes a Hide(); Activate() brings the window to the front.
    static void BringBack()
    {
        var window = ReactorApp.PrimaryWindow;
        window?.Show();
        window?.Activate();
    }
}
// </snippet:tray-menu>

// <snippet:clipboard>
// Reactor has no clipboard wrapper. Call the WinRT Clipboard API
// from a UI-thread event handler.
class ClipboardDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Hello from Reactor");
        var (status, setStatus) = UseState("");

        return VStack(8,
            TextBox(text, setText, header: "Text to copy"),
            HStack(8,
                Button("Copy", () =>
                {
                    var package = new DataPackage();
                    package.SetText(text);
                    try
                    {
                        Clipboard.SetContent(package);
                        setStatus("Copied.");
                    }
                    catch (COMException)
                    {
                        // Another process can hold the clipboard open.
                        setStatus("The clipboard is busy. Try again.");
                    }
                }),
                Button("Paste", async () =>
                {
                    var content = Clipboard.GetContent();
                    if (content.Contains(StandardDataFormats.Text))
                        setText(await content.GetTextAsync());
                })),
            TextBlock(status));
    }
}
// </snippet:clipboard>
