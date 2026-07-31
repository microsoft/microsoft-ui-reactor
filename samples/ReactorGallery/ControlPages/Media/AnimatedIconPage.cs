using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

// `using static Factories` shadows the WinUI type name with the AnimatedIcon(...) factory
// method, so the SetState attached-property helper has to be reached through an alias.
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

namespace WinUIGalleryReactor.ControlPages.Media;

class AnimatedIconPage : Component
{
    // The states every built-in animated visual source ships markers for
    // ("NormalToPointerOver_Start", "PressedToNormal_End", …). A static array keeps the
    // ComboBox items reference-stable across renders.
    static readonly string[] States = ["Normal", "PointerOver", "Pressed"];

    static Element Cell(string label, Element icon) =>
        VStack(6,
            Border(icon.Center()).Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6),
            Caption(label).Foreground(Theme.SecondaryText).Center());

    public override Element Render()
    {
        // UseMemo is load-bearing, not tidiness: AnimatedIcon.Source is a reference-compared
        // one-way binding, so a `new …VisualSource()` built inside Render() would be a fresh
        // instance every render and rebuild the composition visual mid-transition — the
        // animation would be cancelled by the very re-render that asked for it.
        //
        // Settings/Find/GlobalNavigationButton are picked because each ships a non-empty
        // marker segment for all six ordered pairs of the states below, so every value the
        // ComboBox offers plays a real animation. Not every built-in source does: the
        // ChevronDownSmall asset's NormalToPointerOver segment is zero-length (a chevron looks
        // the same hovered), and the ChevronRightDownSmall/ChevronUpDownSmall/Accept assets
        // carry Collapsed/Expanded-style markers instead of these three states entirely.
        var settings = UseMemo(() => new AnimatedSettingsVisualSource());
        var find = UseMemo(() => new AnimatedFindVisualSource());
        var nav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
        var menuNav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());

        var (stateIdx, setStateIdx) = UseState(0);
        var state = States[stateIdx];

        var (hovering, setHovering) = UseState(false);
        // UseReducer, not UseState: the click handler derives the next value from the
        // current one. RequestRender enqueues on the dispatcher and coalesces, so two
        // clicks can run against the same closure before a render replaces it, and
        // setOpen(!open) would write the same value twice and drop a toggle. The
        // functional updater reads the live hook value instead of a captured local.
        var (open, setOpen) = UseReducer(false);
        var menuState = open ? "Pressed" : hovering ? "PointerOver" : "Normal";

        return ScrollView(VStack(16,
            PageHeader("AnimatedIcon",
                "An icon whose animation is a state transition: write AnimatedIcon.State and the "
                + "control plays the \"<from>To<to>\" marker segment baked into its visual source. "
                + "There is no Play() to call. With system animations turned off it hard-cuts to "
                + "the transition's end frame instead."),

            SampleCard("Play a state transition",
                VStack(12,
                    HStack(20,
                        Cell("Settings", AnimatedIcon(settings).Size(32, 32)
                            .Set(icon => XamlAnimatedIcon.SetState(icon, state))),
                        Cell("Find", AnimatedIcon(find).Size(32, 32)
                            .Set(icon => XamlAnimatedIcon.SetState(icon, state))),
                        Cell("Nav", AnimatedIcon(nav).Size(32, 32)
                            .Set(icon => XamlAnimatedIcon.SetState(icon, state)))),
                    Caption($"State: {state} — pick another value to play the transition into it.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;
//   `using static Factories` shadows the WinUI type with the AnimatedIcon factory method.
var settings = UseMemo(() => new AnimatedSettingsVisualSource());
var (stateIdx, setStateIdx) = UseState(0);
var state = new[] { ""Normal"", ""PointerOver"", ""Pressed"" }[stateIdx];

AnimatedIcon(settings).Size(32, 32)
    .Set(icon => XamlAnimatedIcon.SetState(icon, state))
// Each write of State plays the ""<from>To<to>"" marker segment — that transition
// IS the animation. UseMemo the source: Source is reference-compared, so a fresh
// instance per render rebuilds the visual and cancels the transition.
// Check the source's Markers before wiring a state to it: assets built for other
// states leave ""<from>To<to>"" missing or zero-length, and then nothing moves.
// Pass a FallbackIconSource for machines that cannot play Lottie visuals.
",
                OptionPanel(
                    TextBlock("State"),
                    ComboBox(States, stateIdx, setStateIdx))),

            SampleCard("Hover or click a button",
                VStack(12,
                    Button(
                        HStack(8,
                            AnimatedIcon(menuNav).Size(20, 20)
                                .Set(icon => XamlAnimatedIcon.SetState(icon, menuState)),
                            TextBlock(open ? "Close" : "Menu")),
                        () => setOpen(o => !o))
                        .OnPointerEntered((_, _) => setHovering(true))
                        .OnPointerExited((_, _) => setHovering(false)),
                    Caption($"Icon state: {menuState}").Foreground(Theme.SecondaryText),
                    open
                        ? Border(VStack(4,
                                TextBlock("New file"),
                                TextBlock("Open recent"),
                                TextBlock("Save as…")))
                            .Background(Theme.SubtleFill).CornerRadius(6).Padding(12).Width(180)
                        : Border(Caption("Menu is closed — click the button to open it.")
                                .Foreground(Theme.SecondaryText))
                            .Background(Theme.SubtleFill).CornerRadius(6).Padding(12).Width(180)),
                sourceCode: @"
var menuNav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
var (hovering, setHovering) = UseState(false);
// UseReducer's functional updater reads the live value; setOpen(!open) would use the
// captured local, and RequestRender coalesces, so two clicks against one closure would
// write the same value twice and drop a toggle.
var (open, setOpen) = UseReducer(false);
var menuState = open ? ""Pressed"" : hovering ? ""PointerOver"" : ""Normal"";

Button(
    HStack(8,
        AnimatedIcon(menuNav).Size(20, 20)
            .Set(icon => XamlAnimatedIcon.SetState(icon, menuState)),
        TextBlock(open ? ""Close"" : ""Menu"")),
    () => setOpen(o => !o))
    .OnPointerEntered((_, _) => setHovering(true))
    .OnPointerExited((_, _) => setHovering(false))
// XamlAnimatedIcon is a `using` alias for Microsoft.UI.Xaml.Controls.AnimatedIcon —
// `using static Factories` shadows the type name with the AnimatedIcon factory method.
// .OnPointerPressed would never fire here — Button marks PointerPressed handled to
// drive its own Click — so the click handler owns the ""Pressed"" state.
")
        ).Margin(36, 24, 36, 36));
    }
}
