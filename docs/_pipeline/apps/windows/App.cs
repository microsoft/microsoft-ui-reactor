using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Factories;

// <snippet:run>
ReactorApp.Run<WindowsApp>("Windows Demo", width: 640, height: 520
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
                .AutomationName("Document text")
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

// <snippet:window-icon>
// The window icon is the Win32 HICON shown in the taskbar, Alt-Tab, and Task
// Manager — distinct from TitleBar(...).Icon(...), which draws a mark inside
// the window. Use an .ico.
static class WindowIconSetup
{
    // Unpackaged: a file deployed beside the app.
    public static void RunWithFileIcon() =>
        ReactorApp.Run<WindowsApp>("Windows Demo",
            icon: WindowIcon.FromPath("Assets/AppIcon.ico"));

    // Packaged: an .ico shipped with Build Action = Content.
    public static void RunWithPackagedIcon() =>
        ReactorApp.Run<WindowsApp>("Windows Demo",
            icon: WindowIcon.FromResource("ms-appx:///Assets/AppIcon.ico"));

    // A full WindowSpec reaches the fields the flat arguments cannot.
    public static void RunWithSpec() =>
        ReactorApp.Run<WindowsApp>(new WindowSpec
        {
            Title = "Windows Demo",
            Width = 640,
            Height = 520,
            MinWidth = 400,
            Icon = WindowIcon.FromPath("Assets/AppIcon.ico"),
        });
}
// </snippet:window-icon>

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

// ────────────────────────────────────────────────────────────────────
//  Compiled counterparts for the guide's per-section examples.
// ────────────────────────────────────────────────────────────────────

static class WindowLifecycle
{
    // <snippet:open-window>
    public static void OpenSettings()
    {
        var settings = ReactorApp.OpenWindow(
            new WindowSpec { Title = "Settings", Width = 520, Height = 420 },
            () => new SettingsWindow());

        settings.Activate();
        settings.Close();
    }
    // </snippet:open-window>
}

// <snippet:sizing>
class PreviewWindow : Component
{
    public static WindowSpec Spec => new()
    {
        Title = "Preview",
        Width = 640,
        Height = 360,
        ResizeMode = WindowResizeMode.CanMinimize,
        AspectRatio = 16.0 / 9.0,
    };

    public override Element Render()
    {
        var window = UseWindow();

        UseWindowAspectRatio(1.0); // lifetime-bound hook; unmount clears it

        return Button("Widescreen", () => window?.SetAspectRatio(4.0 / 3.0));
    }
}
// </snippet:sizing>

// <snippet:placement>
class CommandPalette : Component
{
    public static WindowSpec Spec => new()
    {
        Title = "Command Palette",
        StartPosition = WindowStartPosition.CenterOnCurrent,
        IsMovableByBackground = true,
    };

    public override Element Render()
    {
        var (x, y) = UseWindowPosition();
        var drag = UseWindowDragMove();

        return VStack(8,
            TextBlock($"at {x}, {y}"),
            Button("Drag window", drag));
    }
}
// </snippet:placement>

// <snippet:drag-regions>
class PaletteChrome : Component
{
    public override Element Render() =>
        HStack(
            TextBlock("Palette"),
            Button("Settings").Drag(false));
}
// </snippet:drag-regions>

static class WindowPlacementPersistence
{
    // <snippet:persistence>
    public static WindowSpec ShellSpec { get; } =
        new WindowSpec { Title = "Shell" }
            .WithPersistence("main-window", fallback: WindowStartPosition.CenterOnCurrent);

    public static void FlushPlacement(ReactorWindow window)
    {
        window.SavePlacement(); // manual best-effort flush
    }
    // </snippet:persistence>
}

// <snippet:z-order>
class FloatingPalette : Component
{
    public static WindowSpec Spec => new()
    {
        Title = "Palette",
        Level = WindowLevel.Floating,
        ShowInTaskbar = false,
        ShowInSwitcher = true,
    };

    public override Element Render()
    {
        var isCovered = UseIsCovered(); // hint from ZOrderChanged
        return TextBlock(isCovered ? "(covered)" : "(visible)");
    }
}
// </snippet:z-order>

static class WindowChrome
{
    // <snippet:chrome>
    public static WindowSpec HudSpec { get; } = new()
    {
        Title = "HUD",
        Style = WindowStyle.None,
        IsMovableByBackground = true,
        CornerStyle = WindowCornerStyle.Rounded,
        Backdrop = BackdropChoice.Of(BackdropKind.DesktopAcrylic),
    };
    // </snippet:chrome>
}

// <snippet:title-bar>
class TitleBarWindow : Component
{
    public override Element Render() =>
        VStack(
            TitleBar("My app"),
            TextBlock("Body"));
}
// </snippet:title-bar>

class TitleBarContentWindow : Component
{
    private static void OnSettings() { }

    public override Element Render() =>
        // <snippet:title-bar-content>
        (TitleBar("Gallery") with
        {
            Content = HStack(8,
                AutoSuggestBox("", _ => {})
                    .AutomationName("Search gallery")
                    .Width(200),
                Button(Icon(FontIcon("\uE713", fontSize: 16)), OnSettings)
                    .AutomationName("Settings").IsDragRegion(false)),
        }).AutoRefreshDragRegions();
        // </snippet:title-bar-content>
}

class TallTitleBarWindow : Component
{
    public override Element Render()
    {
        var nav = UseNavigation("home");

        // <snippet:title-bar-tall>
        var titleBar = TitleBar("My app")
            .WithNavigation(nav)
            .PaneToggleButtonVisible(true)
            .Tall();                                  // or .HeightOption(WindowTitleBarHeight.Tall)
        // </snippet:title-bar-tall>

        return VStack(titleBar, NavigationHost(nav, route => TextBlock(route)));
    }
}

static class TallTitleBarSpec
{
    // <snippet:title-bar-height-spec>
    public static WindowSpec Spec { get; } = new()
    {
        Title = "My app",
        ExtendsContentIntoTitleBar = true,
        TitleBarHeight = WindowTitleBarHeight.Tall,
    };
    // </snippet:title-bar-height-spec>
}

// <snippet:title-bar-tall-legacy>
class LegacyTallTitleBar : Component
{
    public override Element Render()
    {
        // Don't do this any more.
        var window = UseWindow();
        UseEffect(() =>
        {
            if (window is not { } win) return;
            win.NativeWindow?.DispatcherQueue.TryEnqueue(() =>
                win.AppWindow.TitleBar.PreferredHeightOption =
                    Microsoft.UI.Windowing.TitleBarHeightOption.Tall);
        });

        return TitleBar("My app");
    }
}
// </snippet:title-bar-tall-legacy>

class TaskbarDemo : Component
{
    private static void Pause() { }

    public override Element Render()
    {
        // <snippet:taskbar>
        var taskbar = UseWindow()!.TaskbarItem;
        taskbar.Description = "Build in progress";
        taskbar.Progress.State = TaskbarProgressState.Normal;
        taskbar.Progress.Value = 0.42;
        taskbar.SetThumbnailToolbar([
            new ThumbnailToolbarButton("pause", WindowIcon.FromPath("pause.ico"), "Pause", () => Pause())
        ]);
        // </snippet:taskbar>

        return TextBlock("Building...");
    }
}

static class AppJumpList
{
    // <snippet:jump-list>
    // Unpackaged apps must set an AppUserModelId once, before the first
    // UpdateAsync — the shell has no other stable identity to hang the
    // jump list off. Packaged apps inherit it from the manifest.
    public static async Task PublishAsync()
    {
        JumpList.AppUserModelId = "Contoso.Reactor.Demo";
        JumpList.ShowRecent = true;

        await JumpList.UpdateAsync([
            JumpListItem.ForUri("New document", "contoso://new"),
            JumpListItem.ForUri("Open dashboard", "contoso://dashboard",
                description: "Jump straight to the dashboard"),
            new JumpListItem("Report a bug", "contoso://bug",
                Kind: JumpListItemKind.Custom, GroupCategory: "Help"),
        ]);
    }

    // Entries come back as a plain process re-launch. Resolve the argument
    // string through the same DeepLinkMap the app already uses for routes;
    // never act on it unvalidated. DeepLinkResult.Routes is the resolved
    // back stack, deepest route last.
    public static void Start(DeepLinkMap<string> routes) =>
        ReactorApp.Run(ctx =>
        {
            if (ctx.LaunchActivation.Kind == LaunchKind.JumpList &&
                ctx.LaunchActivation.TryResolve(routes, out var deepLink))
            {
                ReactorApp.OpenWindow(
                    new WindowSpec { Title = deepLink.Routes[^1] },
                    () => new SettingsWindow());
            }
        });
    // </snippet:jump-list>
}

class DisplayDemo : Component
{
    public override Element Render()
    {
        var window = UseWindow()!;

        // <snippet:displays>
        var displays = UseDisplays();
        var nearest = ReactorDisplay.NearestTo(window.Position.X, window.Position.Y);
        // </snippet:displays>

        return TextBlock($"{displays.Count} display(s); nearest {nearest.Id}");
    }
}

class PickerDemo : Component
{
    public override Element Render()
    {
        // <snippet:pickers>
        // Both helpers return null when the user cancels the dialog.
        var pickFile = UseFilePickerAsync;
        var pickFolder = UseFolderPickerAsync;

        return Button("Open...", async () =>
        {
            var file = await pickFile(new FilePickerOptions(
                FileTypeFilter: [".txt", ".md"]));
            if (file is null) return;

            var folder = await pickFolder(new FolderPickerOptions());
            if (folder is null) return;
        });
        // </snippet:pickers>
    }
}

static class WindowRegistry
{
    // <snippet:enumerate>
    public static void Inspect(WindowKey key)
    {
        IReadOnlyList<ReactorWindow> all = ReactorApp.Windows; // snapshot
        ReactorWindow? primary = ReactorApp.PrimaryWindow;     // null after it closes
        ReactorWindow? found = ReactorApp.FindWindow(key);     // look up by WindowKey
    }
    // </snippet:enumerate>
}

// ────────────────────────────────────────────────────────────────────
//  Spec 054 windowing migration. The "Before" halves name fields that no
//  longer exist, so they stay as comments; the "After" halves compile.
// ────────────────────────────────────────────────────────────────────

static class Migration054
{
    // <snippet:migration-054-flags>
    // Before
    // new WindowSpec
    // {
    //     Title = "Tools",
    //     IsResizable = false,
    //     IsShownInSwitchers = false,
    //     IsAlwaysOnTop = true,
    // };

    // After
    public static WindowSpec Tools { get; } = new()
    {
        Title = "Tools",
        ResizeMode = WindowResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowInSwitcher = false,
        Level = WindowLevel.AlwaysOnTop,
    };
    // </snippet:migration-054-flags>

    // <snippet:migration-054-persistence>
    // Before
    // new WindowSpec
    // {
    //     Title = "Main",
    //     PersistenceId = "main",
    //     StartPosition = WindowStartPosition.RestoreFromPersistence,
    // };

    // After
    public static WindowSpec Main { get; } =
        new WindowSpec { Title = "Main" }.WithPersistence("main");
    // </snippet:migration-054-persistence>
}
