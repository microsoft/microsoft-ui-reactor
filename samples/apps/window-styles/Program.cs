using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run(_ =>
{
    ReactorApp.OpenWindow(new WindowSpec
    {
        Title = "Window Styles Playground",
        Width = 760,
        Height = 880,
        StartPosition = WindowStartPosition.CenterOnPrimary,
        Backdrop = BackdropChoice.Of(BackdropKind.Mica),
        Icon = WindowIcon.FromPath("Assets/AppIcon.png"),
    }, () => new WindowStylesPlayground());
});

internal sealed class WindowStylesPlayground : Component
{
    // Preset aspect ratios (label, ratio). null = no lock.
    private static readonly (string Label, double? Ratio)[] AspectPresets =
    {
        ("Unlocked",      null),
        ("1 : 1",         1.0),
        ("4 : 3",         4.0 / 3.0),
        ("16 : 9",        16.0 / 9.0),
        ("21 : 9",        21.0 / 9.0),
        ("3 : 4 (tall)",  3.0 / 4.0),
    };

    private static readonly string[] StyleNames     = { "Default", "None", "ToolWindow" };
    private static readonly string[] ResizeNames    = { "CanResize", "CanMinimize", "NoResize" };
    private static readonly string[] CornerNames    = { "Default", "Square", "Rounded", "RoundedSmall" };
    private static readonly string[] LevelNames     = { "Normal", "Floating", "AlwaysOnTop" };
    private static readonly string[] SizeFitNames   = { "Manual", "Width", "Height", "WidthAndHeight" };
    private static readonly string[] BackdropNames  = { "None", "Mica", "MicaAlt", "DesktopAcrylic", "AcrylicThin", "Transparent" };

    public override Element Render()
    {
        var win = UseWindow();
        var (winW, winH) = UseWindowSize();
        // Use UseWindowPosition() (not win?.Position) so the status footer
        // re-renders live while the user drags/moves the window. The hook
        // subscribes to PositionChanged and triggers a re-render on each
        // event, which is the read-back surface this playground showcases.
        var winPos = UseWindowPosition();

        // All adjustable state lives here. Mutators push into the live
        // window via ReactorWindow.Update / targeted setters.
        var (title,            setTitle)            = UseState("Window Styles Playground");
        var (styleIdx,         setStyleIdx)         = UseState(0); // Default
        var (cornerIdx,        setCornerIdx)        = UseState(0); // Default
        var (resizeIdx,        setResizeIdx)        = UseState(0); // CanResize
        var (levelIdx,         setLevelIdx)         = UseState(0); // Normal
        var (sizeFitIdx,       setSizeFitIdx)       = UseState(0); // Manual
        var (aspectIdx,        setAspectIdx)        = UseState(0); // Unlocked
        var (aspectClient,     setAspectClient)     = UseState(false); // false = Window basis, true = Client
        var (backdropIdx,      setBackdropIdx)      = UseState(1); // Mica
        var (movable,          setMovable)          = UseState(false);
        var (showInTaskbar,    setShowInTaskbar)    = UseState(true);
        var (showInSwitcher,   setShowInSwitcher)   = UseState(true);
        var (extendsTitleBar,  setExtendsTitleBar)  = UseState(false);
        var (opacity,          setOpacity)          = UseState(1.0);
        var (taskbarDesc,      setTaskbarDesc)      = UseState("");

        void Apply(Func<WindowSpec, WindowSpec> patch)
        {
            try { win!.Update(patch(win.Spec)); }
            catch (ArgumentException) { /* swallow validation errors triggered mid-toggle */ }
        }

        // --- Cards ----------------------------------------------------------

        Element identityCard = Card("Identity",
            "How the OS labels the window",
            Row("Title", "Caption text (also Alt-Tab label).",
                TextBox(title, v => { setTitle(v); Apply(s => s with { Title = v }); }).Width(280)),
            Row("Taskbar description", "Tooltip shown when hovering the taskbar thumbnail.",
                TextBox(taskbarDesc, v =>
                {
                    setTaskbarDesc(v);
                    try { win!.TaskbarItem.Description = string.IsNullOrEmpty(v) ? null : v; }
                    catch { /* tear-down race */ }
                }, placeholderText: "(no thumbnail tooltip)").Width(280)));

        Element chromeCard = Card("Style & chrome",
            "The window frame, corners, and title-bar surface",
            Row("Style", "Default = standard chrome. None = borderless. ToolWindow = small caption.",
                ComboBox(StyleNames, styleIdx, i => { setStyleIdx(i); Apply(s => s with { Style = (WindowStyle)i }); }).Width(180)),
            Row("Corner style", "DWM corner preference (Win11 only).",
                ComboBox(CornerNames, cornerIdx, i => { setCornerIdx(i); Apply(s => s with { CornerStyle = (WindowCornerStyle)i }); }).Width(180)),
            Row("Extend content into title bar", "Required for a fully custom title bar.",
                ToggleSwitch(extendsTitleBar, v => { setExtendsTitleBar(v); Apply(s => s with { ExtendsContentIntoTitleBar = v }); })));

        Element behaviorCard = Card("Behavior",
            "Resize policy, z-order, and the drag-from-anywhere affordance",
            Row("Resize mode", "Which border / caption buttons the user can interact with.",
                ComboBox(ResizeNames, resizeIdx, i => { setResizeIdx(i); Apply(s => s with { ResizeMode = (WindowResizeMode)i }); }).Width(180)),
            Row("Z-order", "Floating stays above other app windows; AlwaysOnTop uses the Win32 topmost tier.",
                ComboBox(LevelNames, levelIdx, i => { setLevelIdx(i); Apply(s => s with { Level = (WindowLevel)i }); }).Width(180)),
            Row("Drag from background", "Press anywhere non-interactive to drag the window.",
                ToggleSwitch(movable, v => { setMovable(v); Apply(s => s with { IsMovableByBackground = v }); })),
            Row("Show in taskbar", "Whether the window appears on the taskbar.",
                ToggleSwitch(showInTaskbar, v => { setShowInTaskbar(v); Apply(s => s with { ShowInTaskbar = v }); })),
            Row("Show in Alt-Tab", "Whether the window appears in the shell switcher.",
                ToggleSwitch(showInSwitcher, v => { setShowInSwitcher(v); Apply(s => s with { ShowInSwitcher = v }); })));

        Element sizingCard = Card("Sizing",
            "Aspect-ratio lock, size-to-content, and runtime helpers",
            Row("Size to content", "Window auto-sizes to its content (overrides AspectRatio).",
                ComboBox(SizeFitNames, sizeFitIdx, i =>
                {
                    setSizeFitIdx(i);
                    var newFit = (WindowSizeToContent)i;
                    // AspectRatio and SizeToContent are mutually exclusive (spec validation rejects both).
                    Apply(s => s with
                    {
                        SizeToContent = newFit,
                        AspectRatio = newFit == WindowSizeToContent.Manual ? AspectPresets[aspectIdx].Ratio : null,
                    });
                }).Width(180)),
            Row("Aspect ratio", sizeFitIdx == 0
                    ? "Width / height lock applied during interactive resize."
                    : "Disabled while Size-to-content is active.",
                ComboBox(AspectPresets.Select(p => p.Label).ToArray(),
                    aspectIdx, i =>
                    {
                        setAspectIdx(i);
                        if (sizeFitIdx == 0)
                            Apply(s => s with { AspectRatio = AspectPresets[i].Ratio });
                    }).Width(180)),
            Row("Aspect basis: content area", "Off = ratio applies to the outer window rect (default). On = ratio applies to the content area; chrome is auto-accounted for.",
                ToggleSwitch(aspectClient, v =>
                {
                    setAspectClient(v);
                    Apply(s => s with { AspectRatioBasis = v ? AspectRatioBasis.Client : AspectRatioBasis.Window });
                })),
            Row("Recenter on screen", "Re-runs CenterOnPrimary based on the current monitor.",
                Button("Center now", () => win!.CenterOnScreen()).Width(140)),
            Row("Programmatic drag", "Equivalent to clicking the title-bar and dragging.",
                Button("BeginDragMove()", () => win!.BeginDragMove()).Width(180)));

        Element appearanceCard = Card("Appearance",
            "Backdrop material and window-wide opacity",
            Row("Backdrop", "Mica / Acrylic require Windows 11. Transparent needs WinAppSDK support.",
                ComboBox(BackdropNames, backdropIdx, i => setBackdropIdx(i)).Width(200)),
            Row($"Opacity: {opacity:0.00}", "Below 1.0 uses Win32 layered windows.",
                Slider(opacity * 100, 25, 100, v =>
                {
                    var newOpacity = Math.Round(v) / 100.0;
                    setOpacity(newOpacity);
                    try { win!.SetOpacity(newOpacity); }
                    catch { /* window torn down */ }
                }).Width(220)));

        Element statusBar = Border(
            HStack(24,
                StatusLabel("Size",     $"{winW:0} × {winH:0} DIP"),
                StatusLabel("Position", $"({winPos.X:0}, {winPos.Y:0})"),
                StatusLabel("DPI",      $"{(win?.Dpi ?? 96):0}"),
                StatusLabel("State",    (win?.State.ToString() ?? "—"))))
            .Background("#1A000000")
            .Padding(16, 12, 16, 12);

        Element root = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Auto, GridSize.Star(), GridSize.Auto },
            // Header
            Border(VStack(4,
                    TextBlock("Window Styles Playground")
                        .FontSize(26).Bold(),
                    TextBlock("Tweak every spec-054 window property and watch the live window react.")
                        .FontSize(14).Opacity(0.7)))
                .Padding(28, 28, 28, 16)
                .Grid(row: 0, column: 0),
            // Scrollable settings cards (takes the Star row so it fills remaining space)
            ScrollViewer(VStack(12,
                identityCard,
                chromeCard,
                behaviorCard,
                sizingCard,
                appearanceCard,
                Border(null).Height(24))
                .Padding(28, 0, 28, 0))
                .Grid(row: 1, column: 0),
            // Status footer pinned to bottom
            statusBar.Grid(row: 2, column: 0));

        // Backdrop is the only property set declaratively (modifier-driven)
        // rather than via WindowSpec.Update — apply it on the root tree.
        return ApplyBackdrop(root, backdropIdx);
    }

    private static Element ApplyBackdrop(Element root, int backdropIdx) => backdropIdx switch
    {
        0 => root.Backdrop(BackdropKind.None),
        1 => root.Backdrop(BackdropKind.Mica),
        2 => root.Backdrop(BackdropKind.MicaAlt),
        3 => root.Backdrop(BackdropKind.DesktopAcrylic),
        4 => root.Backdrop(BackdropKind.AcrylicThin),
        5 => root.Backdrop(BackdropKind.Transparent),
        _ => root,
    };

    // --- Win11 settings-page-style helpers --------------------------------

    private static Element Card(string title, string subtitle, params Element[] rows)
    {
        var rowList = new List<Element>(rows.Length * 2);
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) rowList.Add(Border(null).Height(1).Background("#14FFFFFF"));
            rowList.Add(rows[i]);
        }
        return Border(VStack(0,
                Border(VStack(2,
                        TextBlock(title).FontSize(16).Bold(),
                        TextBlock(subtitle).FontSize(12).Opacity(0.6)))
                    .Padding(20, 16, 20, 12),
                Border(VStack(0, rowList.ToArray()))
                    .Padding(20, 0, 20, 8)))
            .CornerRadius(8)
            .Background("#15FFFFFF")
            .WithBorder("#22FFFFFF", 1);
    }

    private static Element Row(string label, string hint, Element control)
        => Border(Grid(
                columns: new[] { GridSize.Star(), GridSize.Auto },
                rows:    new[] { GridSize.Auto },
                VStack(2,
                    TextBlock(label).FontSize(14),
                    TextBlock(hint).FontSize(12).Opacity(0.55))
                    .Grid(row: 0, column: 0),
                control.Grid(row: 0, column: 1)))
            .Padding(0, 12, 0, 12);

    private static Element StatusLabel(string key, string value)
        => VStack(2,
            TextBlock(key).FontSize(11).Opacity(0.55),
            TextBlock(value).FontSize(13).Bold());
}
