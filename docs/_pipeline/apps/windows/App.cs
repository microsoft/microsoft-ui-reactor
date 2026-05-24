using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// <snippet:run>
ReactorApp.Run<WindowsApp>("Windows Demo", width: 640, height: 520
#if DEBUG
    , preview: true
#endif
);
// </snippet:run>

// <snippet:shell>
class WindowsApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            Heading("Top-level Windows"),
            HStack(8,
                Button("New Notepad window", () =>
                {
                    var n = count + 1;
                    ReactorApp.OpenWindow(
                        new WindowSpec
                        {
                            Title = $"Notepad #{n}",
                            Width = 420,
                            Height = 300,
                        },
                        () => new NotePadWindow($"Document #{n}"));
                    setCount(n);
                }),
                Button("Open settings", () =>
                {
                    // Reuse the same window if it's already open: FindWindow
                    // looks the surface up by its WindowKey, so a second
                    // click brings the existing window forward instead of
                    // opening a duplicate.
                    var key = WindowKey.Of("settings");
                    var existing = ReactorApp.FindWindow(key);
                    if (existing is not null)
                    {
                        existing.Activate();
                        return;
                    }

                    ReactorApp.OpenWindow(
                        new WindowSpec
                        {
                            Title = "Settings",
                            Width = 480,
                            Height = 360,
                            Key = key,
                        },
                        () => new SettingsWindow());
                })
            ),
            TextBlock($"Open windows: {ReactorApp.Windows.Count}")
        ).Padding(20);
    }
}
// </snippet:shell>

// <snippet:notepad-window>
class NotePadWindow : Component
{
    private readonly string _label;
    public NotePadWindow(string label) { _label = label; }

    public override Element Render()
    {
        var (text, setText) = UseState("");
        var window = UseWindow();
        var state = UseWindowState();

        return VStack(12,
            SubHeading(_label),
            TextBlock(window is null
                ? "(no owning window)"
                : $"id={window.Id}  state={state}  dpi={window.Dpi}"),
            TextBox(text, setText, placeholderText: "Type something...")
                .Width(360),
            Button("Close", () => window?.Close())
        ).Padding(16);
    }
}
// </snippet:notepad-window>

// <snippet:use-open-window>
class SettingsHost : Component
{
    public override Element Render()
    {
        // While this component is mounted, ensure a settings window keyed
        // to "settings" is open. Re-renders that pass the same WindowKey
        // reuse the same handle; the hook dedupes against the live window
        // registry via FindWindow.
        var settings = UseOpenWindow(
            key: "settings",
            spec: new WindowSpec { Title = "Settings", Width = 480, Height = 360 },
            factory: () => new SettingsWindow());

        return TextBlock(settings is null
            ? "(no UI dispatcher)"
            : $"Settings open — id={settings.Id}");
    }
}
// </snippet:use-open-window>

class SettingsWindow : Component
{
    public override Element Render()
    {
        var window = UseWindow();
        return VStack(12,
            Heading("Settings"),
            TextBlock("Pretend there's a preferences pane here."),
            Button("Close", () => window?.Close())
        ).Padding(20);
    }
}

// <snippet:shutdown-policy>
// Call once at startup, before ReactorApp.Run. With OnLastSurfaceClosed the
// process keeps running while a tray icon or any window is alive; with
// Explicit you must call ReactorApp.Exit() yourself.
static class Startup
{
    public static void ConfigureShutdown()
    {
        ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
    }
}
// </snippet:shutdown-policy>

// <snippet:tray-icon>
class TrayHost : Component
{
    public override Element Render()
    {
        var icon = UseMemo(() => WindowIcon.FromPath("Assets/TrayIcon.ico"));
        var tray = UseTrayIcon(new TrayIconSpec(
            Icon: icon,
            Tooltip: "My App",
            Key: WindowKey.Of("main-tray")));

        UseEffect(() =>
        {
            if (tray is null) return () => { };
            void onClick(object? s, EventArgs e)
                => ReactorApp.PrimaryWindow?.Activate();
            tray.Click += onClick;
            return () => tray.Click -= onClick;
        }, tray ?? (object)"no-tray");

        return TextBlock("Tray icon registered while this component is mounted.");
    }
}
// </snippet:tray-icon>
