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
    // States all three sources below ship markers for.
    static readonly string[] States = ["Normal", "PointerOver", "Pressed"];

    static readonly string[] CellNames = ["Settings", "Find", "Nav"];

    public override Element Render()
    {
        // AnimatedIcon.Source is reference-compared: a source built inline would be a new
        // instance every render and would cancel the transition it is meant to play.
        var settings = UseMemo(() => new AnimatedSettingsVisualSource());
        var find = UseMemo(() => new AnimatedFindVisualSource());
        var nav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
        // A different glyph from the gear above, so the two cards are never confused for
        // each other: this one responds only to the picker, those only to the pointer.
        var picker = UseMemo(() => new AnimatedFindVisualSource());
        var menuNav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());

        // Tracked per cell (-1 = none) so hovering one icon doesn't animate its neighbours.
        var (hoverIdx, setHoverIdx) = UseState(-1);
        var (pressIdx, setPressIdx) = UseState(-1);

        // Pointer- and focus-driven only. Mixing in a second driver -- an explicit "resting
        // state" picker, say -- makes hover a no-op whenever the two agree on a value, because
        // writing State the value it already holds plays no segment. That card is separate.
        //
        // A Button rather than a bare Border: it is unambiguously hit-testable, it looks
        // interactive, and it is reachable by Tab. Hover is a mouse-only affordance, so the
        // focus arms below are what make this demo work for keyboard users at all.
        Element Cell(int index, string label, object source)
        {
            var cellState = pressIdx == index ? "Pressed"
                : hoverIdx == index ? "PointerOver"
                : "Normal";

            return VStack(6,
                Button(AnimatedIcon(source).Size(32, 32)
                            .Set(icon => XamlAnimatedIcon.SetState(icon, cellState)),
                        () => { })
                    .Size(72, 56)
                    .OnPointerEntered((_, _) => setHoverIdx(index))
                    .OnPointerExited((_, _) => { setHoverIdx(-1); setPressIdx(-1); })
                    .OnPointerPressed((_, _) => setPressIdx(index))
                    .OnPointerReleased((_, _) => setPressIdx(-1))
                    .OnGotFocus((_, _) => setHoverIdx(index))
                    .OnLostFocus((_, _) => { setHoverIdx(-1); setPressIdx(-1); }),
                Caption(label).Foreground(Theme.SecondaryText).Center());
        }

        var (stateIdx, setStateIdx) = UseState(0);
        var state = States[Math.Clamp(stateIdx, 0, States.Length - 1)];

        var (hovering, setHovering) = UseState(false);
        var (open, setOpen) = UseReducer(false);
        var menuState = open ? "Pressed" : hovering ? "PointerOver" : "Normal";

        // With animations off the samples still change State, but AnimatedIcon hard-cuts
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

            SampleCard("Hover or press an icon",
                VStack(12,
                    HStack(20,
                        Cell(0, "Settings", settings),
                        Cell(1, "Find", find),
                        Cell(2, "Nav", nav)),
                    Caption(hoverIdx < 0 && pressIdx < 0
                            ? "All three are resting on Normal — hover or Tab to one to play its transition."
                            : $"{CellNames[pressIdx >= 0 ? pressIdx : hoverIdx]}: "
                              + (pressIdx >= 0 ? "Pressed" : "PointerOver"))
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// `using static Factories` shadows the WinUI type, so SetState needs an alias:
//   using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;
var source = UseMemo(() => new AnimatedSettingsVisualSource());
var (hovering, setHovering) = UseState(false);
var (pressing, setPressing) = UseState(false);
var state = pressing ? ""Pressed"" : hovering ? ""PointerOver"" : ""Normal"";

// A Button, not a bare Border: unambiguously hit-testable, and reachable by Tab.
// Hover is mouse-only, so the focus arms are what make this work from the keyboard.
Button(AnimatedIcon(source).Size(32, 32)
            .Set(icon => XamlAnimatedIcon.SetState(icon, state)),
        () => { })
    .Size(72, 56)
    .OnPointerEntered((_, _) => setHovering(true))
    .OnPointerExited((_, _) => { setHovering(false); setPressing(false); })
    .OnPointerPressed((_, _) => setPressing(true))
    .OnPointerReleased((_, _) => setPressing(false))
    .OnGotFocus((_, _) => setHovering(true))
    .OnLostFocus((_, _) => { setHovering(false); setPressing(false); })
// Each write of State plays the ""<from>To<to>"" marker segment — that transition IS the
// animation; there is no Play(). UseMemo the source: Source is reference-compared, so a
// fresh instance per render cancels the transition.
"),

            SampleCard("Set the state directly",
                VStack(12,
                    Border(AnimatedIcon(picker).Size(32, 32)
                            .Set(icon => XamlAnimatedIcon.SetState(icon, state))
                            .Center())
                        .Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6),
                    Caption($"State: {state} — pick another value to play the transition into it.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var picker = UseMemo(() => new AnimatedFindVisualSource());
var states = new[] { ""Normal"", ""PointerOver"", ""Pressed"" };
var (stateIdx, setStateIdx) = UseState(0);
var state = states[Math.Clamp(stateIdx, 0, states.Length - 1)];

AnimatedIcon(picker).Size(32, 32)
    .Set(icon => XamlAnimatedIcon.SetState(icon, state))
// State is just a property, so anything can drive it — but keep it to one driver per icon.
// A second driver that can agree on a value makes the other a silent no-op, since writing
// State the value it already holds plays no segment at all.
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
// UseReducer's functional updater reads the live value, so two clicks coalesced into
// one render can't drop a toggle.
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
// Button marks PointerPressed handled to drive its own Click, so the click handler —
// not OnPointerPressed — owns the ""Pressed"" state.
")
        ).Margin(36, 24, 36, 36));
    }
}
