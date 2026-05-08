using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

// Spec 036 — Windows / tray-icon demo.
//
// Demonstrates:
//   • Opening a secondary ReactorWindow on demand (UseOpenWindow keyed reuse).
//   • Spawning many independent windows in a counter-style document pattern.
//   • Registering a tray icon scoped to the demo component (UseTrayIcon).
//
// This page lives inside the existing tabbed TestApp shell — it's NOT a full
// "multi-window document type app." That story lives in
// `c:\temp\window-play\program.cs` (a single-file runnable sample) so you can
// see the whole shutdown-policy + Exit-menu pattern start-to-finish.

class WindowsDemo : Component
{
    // The tab page tracks how many ad-hoc windows it spawned, so a label
    // shows "Open #3" / "Open #4" etc. Each ad-hoc window is independent
    // and stays alive after this page unmounts.
    public override Element Render()
    {
        var (childCount, setChildCount) = UseState(0);
        var (showSecondary, setShowSecondary) = UseState(false);

        // Icons are stable for the life of this component — memoize so a
        // re-render doesn't reallocate the WindowIcon record (and so any
        // future record-equality compare on WindowSpec sees the same
        // reference and skips ApplyChrome). UseMemo with no deps == compute
        // once on first render, reuse forever.
        var appIcon = UseMemo(() => WindowIcon.FromPath(global::System.IO.Path.Combine(
            global::System.AppContext.BaseDirectory, "Assets", "AppIcon.ico")));

        // 1) Keyed secondary window — imperative open/close. We can't gate a
        //    UseOpenWindow call behind `showSecondary` because that changes
        //    the hook count between renders (HookOrderException). Instead
        //    keep a UseRef of the live handle and reconcile open/close in a
        //    UseEffect keyed on `showSecondary`. The hook surface stays
        //    constant; the imperative side-effect tracks the toggle.
        //
        //    The trickier piece: when the user closes the window themselves
        //    (internal "Close this window" button or the system X), our
        //    `showSecondary` state stays `true` — so the next tray click
        //    sets state to a value it already has, no re-render fires, and
        //    nothing opens. We listen on the window's Closed event and
        //    flip `showSecondary` back to false so the next tray click
        //    is a real state transition.
        var keyedWindowRef = UseRef<ReactorWindow?>(null);
        UseEffect(() =>
        {
            // Always re-resolve through the registry so a window closed
            // externally doesn't leave us pointing at a disposed handle.
            var existing = ReactorApp.FindWindow(WindowKey.Of("testapp-keyed"));
            keyedWindowRef.Current = existing;

            ReactorWindow? opened = null;
            EventHandler? onClosed = null;
            if (showSecondary && existing is null)
            {
                opened = ReactorApp.OpenWindow(
                    new WindowSpec
                    {
                        Title = "Keyed Secondary Window",
                        Width = 480,
                        Height = 320,
                        Icon = appIcon,
                        Key = WindowKey.Of("testapp-keyed"),
                    },
                    () => new SecondaryWindowContent("Keyed window — same handle while showSecondary is true"));
                keyedWindowRef.Current = opened;
                onClosed = (_, _) => setShowSecondary(false);
                opened.Closed += onClosed;
            }
            else if (!showSecondary && existing is not null)
            {
                existing.Close();
                keyedWindowRef.Current = null;
            }

            // Cleanup runs when deps change next time (or on unmount).
            // Unsub the Closed handler we just attached — try/catch covers
            // the disposed-window case where -= can throw.
            return () =>
            {
                if (opened is not null && onClosed is not null)
                {
                    try { opened.Closed -= onClosed; } catch { /* best effort */ }
                }
            };
        }, showSecondary, appIcon);
        var keyedWindow = keyedWindowRef.Current;

        // 2) Tray icon scoped to this component. Loads a real .ico shipped
        //    next to the exe (Assets/TrayIcon.ico — borrowed from the
        //    MIT-licensed microsoft-ui-xaml WinUI Gallery sample). Swap
        //    in your own .ico to see your own glyph in the tray.
        //
        //    Memoize the path + WindowIcon record so we don't reallocate
        //    them per render. The TrayIconSpec record itself we DON'T
        //    memoize — its value-equality (compared by UseTrayIcon's
        //    UseEffect) means an identical-by-value spec triggers no
        //    Update call, so a fresh record per render is cheap.
        var trayIconPath = UseMemo(() => global::System.IO.Path.Combine(
            global::System.AppContext.BaseDirectory, "Assets", "TrayIcon.ico"));
        var trayIcon = UseMemo(() => WindowIcon.FromPath(trayIconPath), trayIconPath);
        var tray = UseTrayIcon(new TrayIconSpec(
            Icon: trayIcon,
            Tooltip: "Reactor TestApp — WindowsDemo tab",
            Key: WindowKey.Of("testapp-tray")));

        // Subscribe once: clicking the tray icon flashes the keyed window
        // open. UseEffect cleanup unsubscribes on unmount / when `tray`
        // changes identity.
        UseEffect(() =>
        {
            if (tray is null) return () => { };
            EventHandler onClick = (_, _) => setShowSecondary(true);
            EventHandler onRight = (_, _) => setShowSecondary(false);
            tray.Click += onClick;
            tray.RightClick += onRight;
            return () =>
            {
                tray.Click -= onClick;
                tray.RightClick -= onRight;
            };
        }, tray ?? (object)"no-tray");

        return VStack(16,
            Heading("Windows & Tray Icon"),
            TextBlock("Spec 036 surfaces. Leaving this tab closes the tray icon — UseTrayIcon is component-scoped.")
                .Foreground(TertiaryText),

            // ─── Keyed secondary window ───
            Border(VStack(8,
                SubHeading("Keyed secondary window (UseOpenWindow)"),
                TextBlock("One window. Same handle every render. Toggle to open / close.")
                    .Foreground(TertiaryText),
                HStack(8,
                    Button(showSecondary ? "Close keyed window" : "Open keyed window",
                        () => setShowSecondary(!showSecondary)),
                    TextBlock(keyedWindow is null
                        ? "(closed)"
                        : $"open — id={keyedWindow.Id}")
                        .Foreground(TertiaryText)
                )
            ).Padding(12)).Background(CardBackground).CornerRadius(8),

            // ─── Ad-hoc windows ───
            Border(VStack(8,
                SubHeading("Ad-hoc windows (ReactorApp.OpenWindow)"),
                TextBlock("Each click spawns a new independent window. Closing them does not affect this tab.")
                    .Foreground(TertiaryText),
                HStack(8,
                    Button($"Spawn window #{childCount + 1}", () =>
                    {
                        var n = childCount + 1;
                        ReactorApp.OpenWindow(
                            new WindowSpec
                            {
                                Title = $"Document #{n}",
                                Width = 360,
                                Height = 240,
                                Icon = appIcon,
                            },
                            () => new SecondaryWindowContent($"This is document #{n}"));
                        setChildCount(n);
                    }),
                    Button("Close all secondaries", () =>
                    {
                        // Snapshot first — Close mutates the live array.
                        var toClose = new List<ReactorWindow>();
                        foreach (var w in ReactorApp.Windows)
                        {
                            // Don't close the primary — it's the TestApp shell itself.
                            if (!ReferenceEquals(w, ReactorApp.PrimaryWindow))
                                toClose.Add(w);
                        }
                        foreach (var w in toClose) w.Close();
                        setChildCount(0);
                    }).Disabled(ReactorApp.Windows.Count <= 1)
                ),
                TextBlock($"Total open windows: {ReactorApp.Windows.Count}")
                    .Foreground(TertiaryText)
            ).Padding(12)).Background(CardBackground).CornerRadius(8),

            // ─── Tray icon status ───
            Border(VStack(8,
                SubHeading("Tray icon (UseTrayIcon, component-scoped)"),
                TextBlock(tray is null
                    ? "Tray icon not available — UI dispatcher missing or shell COM unavailable."
                    : $"Tray icon registered — id={tray.Id}, key={tray.Key}")
                    .Foreground(TertiaryText),
                TextBlock("Left-click the icon: opens the keyed window above. Right-click: closes it.")
                    .Foreground(TertiaryText),
                TextBlock($"Icon source: {trayIconPath}")
                    .Foreground(TertiaryText)
            ).Padding(12)).Background(CardBackground).CornerRadius(8)
        );
    }
}

class SecondaryWindowContent : Component
{
    private readonly string _label;
    public SecondaryWindowContent(string label) { _label = label; }

    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var window = UseWindow(); // null when not in a ReactorWindow

        return VStack(16,
            Heading(_label),
            TextBlock(window is null
                ? "(no owning window)"
                : $"id={window.Id}, dpi={window.Dpi}, scale={window.DipScale:F2}"),
            HStack(8,
                Button($"Click count: {count}", () => setCount(count + 1)),
                Button("Reset", () => setCount(0))
            ),
            Button("Close this window", () => window?.Close())
        ).Padding(20);
    }
}
