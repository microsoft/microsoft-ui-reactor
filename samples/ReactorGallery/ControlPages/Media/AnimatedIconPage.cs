using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

// `using static Factories` shadows the WinUI type name, so SetState needs an alias.
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

namespace WinUIGalleryReactor.ControlPages.Media;

class AnimatedIconPage : Component
{
    // States all three sources below ship markers for. Static keeps the ComboBox
    // items reference-stable across renders.
    static readonly string[] States = ["Normal", "PointerOver", "Pressed"];

    static Element Cell(string label, Element icon) =>
        VStack(6,
            Border(icon.Center()).Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6),
            Caption(label).Foreground(Theme.SecondaryText).Center());

    public override Element Render()
    {
        // UseMemo is load-bearing: AnimatedIcon.Source is reference-compared, so a source
        // built inline would be a new instance every render and would rebuild the
        // composition visual mid-transition, cancelling the animation.
        var settings = UseMemo(() => new AnimatedSettingsVisualSource());
        var find = UseMemo(() => new AnimatedFindVisualSource());
        var nav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
        var menuNav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());

        var (stateIdx, setStateIdx) = UseState(0);
        // A controlled ComboBox passes WinUI's -1 ("nothing selected") straight through.
        var state = States[Math.Clamp(stateIdx, 0, States.Length - 1)];

        var (hovering, setHovering) = UseState(false);
        // UseReducer, not UseState: the functional updater reads the live value, so two
        // clicks coalesced into one render can't both write the same value and drop a toggle.
        var (open, setOpen) = UseReducer(false);
        var menuState = open ? "Pressed" : hovering ? "PointerOver" : "Normal";

        // With animations off, every sample still changes State but AnimatedIcon hard-cuts
        // to each segment's end frame — say so rather than appear broken (#983).
        var reducedMotion = UseReducedMotion();

        return ScrollView(VStack(16,
            PageHeader("AnimatedIcon",
                "An icon whose animation is a state transition: write AnimatedIcon.State and the "
                + "control plays the \"<from>To<to>\" marker segment baked into its visual source. "
                + "There is no Play() to call. With system animations turned off it hard-cuts to "
                + "the transition's end frame instead."),

            reducedMotion
                ? InfoBar("System animation effects are turned off",
                        "Every sample on this page still changes State, but AnimatedIcon jumps "
                        + "straight to each transition's end frame instead of playing it. Turn on "
                        + "Settings → Accessibility → Visual effects → Animation effects to watch "
                        + "the transitions animate — this page updates as soon as you do.")
                    .Warning()
                    .IsClosable(false)
                : null,

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
var states = new[] { ""Normal"", ""PointerOver"", ""Pressed"" };
var (stateIdx, setStateIdx) = UseState(0);
// A controlled ComboBox passes WinUI's -1 (""nothing selected"") straight through.
var state = states[Math.Clamp(stateIdx, 0, states.Length - 1)];

AnimatedIcon(settings).Size(32, 32)
    .Set(icon => XamlAnimatedIcon.SetState(icon, state))
// Each write of State plays the ""<from>To<to>"" marker segment — that transition IS
// the animation; there is no Play(). UseMemo the source: Source is reference-compared,
// so a fresh instance per render rebuilds the visual and cancels the transition.
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
// captured local and drop a toggle when two clicks coalesce into one render.
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
// .OnPointerPressed would never fire here — Button marks PointerPressed handled to
// drive its own Click — so the click handler owns the ""Pressed"" state.
")
        ).Margin(36, 24, 36, 36));
    }
}
