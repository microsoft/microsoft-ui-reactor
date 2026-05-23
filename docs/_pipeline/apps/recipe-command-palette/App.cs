using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<CommandPaletteRecipeApp>("Command Palette Recipe", width: 520, height: 420
#if DEBUG
    , preview: true
#endif
);

class CommandPaletteRecipeApp : Component
{
    public override Element Render() => Component<CommandPalette>();
}

class CommandPalette : Component
{
    // <snippet:commands>
    // The catalog is a static array of Reactor Command records. The same
    // record could just as well be bound to a Button or a MenuItem — the
    // palette is one more surface that consumes it.
    private static readonly Command[] Catalog = new[]
    {
        new Command { Label = "File: New",        Execute = () => Log("new") },
        new Command { Label = "File: Open…",      Execute = () => Log("open") },
        new Command { Label = "File: Save",       Execute = () => Log("save") },
        new Command { Label = "Edit: Find",       Execute = () => Log("find") },
        new Command { Label = "Edit: Replace",    Execute = () => Log("replace") },
        new Command { Label = "View: Toggle Theme", Execute = () => Log("theme") },
        new Command { Label = "View: Zen Mode",   Execute = () => Log("zen") },
        new Command { Label = "Go: Go to Line…",  Execute = () => Log("goto-line") },
        new Command { Label = "Go: Go to Symbol…",Execute = () => Log("goto-symbol") },
        new Command { Label = "Help: About",      Execute = () => Log("about") },
    };

    private static void Log(string id) { /* hook to telemetry in a real app */ }
    // </snippet:commands>

    public override Element Render()
    {
        // <snippet:state>
        // Three pieces of state run the palette: whether it's open, the
        // typed query, and the highlighted row in the filtered list.
        var (open, setOpen) = UseState(false);
        var (query, setQuery) = UseState("");
        var (index, setIndex) = UseState(0);
        var (last, setLast) = UseState<string?>(null);
        // </snippet:state>

        // <snippet:filter>
        // Re-derive the filtered list on every render. The catalog is
        // small; a real palette with hundreds of commands would key this
        // through UseMemo on `query`.
        var matches = string.IsNullOrWhiteSpace(query)
            ? Catalog
            : Catalog.Where(c => c.Label.Contains(query,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        // Clamp the selection so it never points off the end of the list.
        var safeIndex = matches.Length == 0
            ? 0
            : Math.Clamp(index, 0, matches.Length - 1);
        // </snippet:filter>

        // <snippet:keyhandler>
        // Esc closes; Up / Down move the selection; Enter invokes the
        // highlighted command. The OnPreviewKeyDown handler intercepts
        // before the TextField gets the keystroke, so arrow keys move
        // the list instead of the caret.
        void OnPaletteKey(object _, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    setOpen(false);
                    e.Handled = true;
                    break;
                case VirtualKey.Down:
                    if (matches.Length > 0)
                        setIndex((safeIndex + 1) % matches.Length);
                    e.Handled = true;
                    break;
                case VirtualKey.Up:
                    if (matches.Length > 0)
                        setIndex((safeIndex - 1 + matches.Length) % matches.Length);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    if (matches.Length > 0)
                    {
                        var cmd = matches[safeIndex];
                        cmd.Execute?.Invoke();
                        setLast(cmd.Label);
                        setOpen(false);
                        setQuery("");
                        setIndex(0);
                    }
                    e.Handled = true;
                    break;
            }
        }
        // </snippet:keyhandler>

        // <snippet:render>
        // The page is rendered normally; the palette is a conditional
        // overlay on top, just like the modal-dialog recipe. The root
        // surface owns the Ctrl+K accelerator so the palette can open
        // from anywhere on the page.
        var page = VStack(12,
            Heading("Command Palette Demo"),
            TextBlock("Press Ctrl+K to open the palette.").Opacity(0.7),
            last is null
                ? Empty()
                : TextBlock($"Last command: {last}").Opacity(0.6)
        ).Padding(24);

        Element palette = Border(
            VStack(0,
                TextBox(query, v => { setQuery(v); setIndex(0); },
                    placeholder: "Type a command…").Width(420),
                matches.Length == 0
                    ? TextBlock("No commands match.").Padding(12).Opacity(0.6)
                    : VStack(0,
                        matches.Select((c, i) =>
                            TextBlock(c.Label)
                                .Padding(10)
                                .Background(i == safeIndex ? "#E5F1FB" : "#FFFFFF")
                        ).ToArray<Element>()
                    )
            ).Background("#FFFFFF").CornerRadius(8).Width(440)
        ).Background("#80000000").Padding(60)
         .OnPreviewKeyDown(OnPaletteKey);

        var root = (open ? Group(page, palette) : page)
            .OnKeyDown((_, e) =>
            {
                // Ctrl+K toggles the palette. A real app would prefer a
                // KeyboardAccelerator on the window root; this keeps the
                // recipe self-contained.
                var ctrl = (Microsoft.UI.Xaml.Window.Current?.CoreWindow
                    .GetKeyState(VirtualKey.Control)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down)
                    == Windows.UI.Core.CoreVirtualKeyStates.Down;
                if (ctrl && e.Key == VirtualKey.K)
                {
                    setOpen(!open);
                    e.Handled = true;
                }
            });
        return root;
        // </snippet:render>
    }
}
