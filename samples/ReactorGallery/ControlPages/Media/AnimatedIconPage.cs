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

    static readonly string[] CellNames = ["Settings", "Find", "Menu"];

    public override Element Render()
    {
        // AnimatedIcon.Source is reference-compared: a source built inline would be a new
        // instance every render and would cancel the transition it is meant to play.
        var settingsGlyph = UseMemo(() => new AnimatedSettingsVisualSource());
        var findGlyph = UseMemo(() => new AnimatedFindVisualSource());
        var navGlyph = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
        // A different glyph keeps the picker card from being mistaken for one of the cells above.
        var picker = UseMemo(() => new AnimatedFindVisualSource());
        var menuNav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());

        // Tracked per cell (-1 = none) so pressing one icon doesn't animate its neighbours.
        var (pressIdx, setPressIdx) = UseState(-1);

        // Press-driven, hosted in a Border. Both are load-bearing, and both were measured on this
        // page rather than reasoned about — an earlier revision of this sample did the opposite of
        // each and demonstrated nothing, which is issue #983 all over again:
        //
        //   Normal<->PointerOver renders no visible change on any of these built-in glyphs. The
        //   marker segment exists and has real duration (0.075 for Settings, 0.1125 for Find and
        //   GlobalNavigationButton), so a test that inspects the timeline passes — and the artwork
        //   is identical at both ends. Driving a Settings icon Normal->PointerOver changed 0 pixels
        //   across 298 frame-pairs, against an idle control reading 0 on the same region.
        //   Normal<->Pressed is the transition these glyphs actually draw.
        //
        //   Hosting the icon in a Button made the same Normal->Pressed write animate only
        //   intermittently, where a Border animated it every time — a Button runs its own
        //   Normal/PointerOver/Pressed visual states over the same content, and racing them is not
        //   worth it in a sample whose whole job is to show a transition. The Border's Background
        //   is load-bearing too: a null-background element is hit-test invisible, so the pointer
        //   handlers below would never fire.
        //
        // One driver only. Adding a second -- an explicit "resting state" picker, say -- makes the
        // press a silent no-op whenever the two agree on a value, because writing State the value
        // it already holds plays no segment. That picker is a separate card for exactly that reason.
        Element Cell(int index, string label, object source)
        {
            var cellState = pressIdx == index ? "Pressed" : "Normal";

            return VStack(6,
                Border(HStack(AnimatedIcon(source).Size(32, 32)
                        .Set(icon =>
                        {
                            // Writing the value it already holds plays no segment, so don't.
                            if (XamlAnimatedIcon.GetState(icon) != cellState)
                            {
                                XamlAnimatedIcon.SetState(icon, cellState);
                            }
                        })))
                    .Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6)
                    .OnPointerPressed((_, _) => setPressIdx(index))
                    .OnPointerReleased((_, _) => setPressIdx(-1))
                    .OnPointerExited((_, _) => setPressIdx(-1)),
                Caption(label).Foreground(Theme.SecondaryText).Center());
        }

        var (stateIdx, setStateIdx) = UseState(0);
        var state = States[Math.Clamp(stateIdx, 0, States.Length - 1)];
        var (open, setOpen) = UseReducer(false);
        var menuState = open ? "Pressed" : "Normal";

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

            SampleCard("Press an icon",
                VStack(12,
                    HStack(20,
                        Cell(0, "Settings", settingsGlyph),
                        Cell(1, "Find", findGlyph),
                        Cell(2, "Menu", navGlyph)),
                    Caption(pressIdx < 0
                            ? "All three are resting on Normal — press and hold one to play its transition."
                            : $"{CellNames[pressIdx]}: Pressed")
                        .Foreground(Theme.SecondaryText),
                    Caption("Normal↔Pressed is used rather than Normal↔PointerOver because these "
                            + "built-in glyphs draw the same artwork at both ends of their hover "
                            + "segment: the marker has duration, so it reads as a real animation to "
                            + "any code that inspects it, and nothing moves on screen.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// `using static Factories` imports an AnimatedIcon() method that wins simple-name lookup,
// so reaching the WinUI type's static SetState needs an alias:
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

var source = UseMemo(() => new AnimatedSettingsVisualSource());
var (pressing, setPressing) = UseState(false);
var state = pressing ? ""Pressed"" : ""Normal"";

// A Border, not a Button: hosting the icon in a Button made the same write animate only
// intermittently, because the Button runs its own Normal/PointerOver/Pressed visual states
// over the same content. Background is load-bearing too -- a null-background element is
// hit-test invisible, so the pointer events below would never fire.
Border(HStack(AnimatedIcon(source).Size(32, 32)
        .Set(icon =>
        {
            // Writing the value it already holds plays no segment, so don't.
            if (XamlAnimatedIcon.GetState(icon) != state)
                XamlAnimatedIcon.SetState(icon, state);
        })))
    .Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6)
    .OnPointerPressed((_, _) => setPressing(true))
    .OnPointerReleased((_, _) => setPressing(false))
    .OnPointerExited((_, _) => setPressing(false))
// Each write of State plays the ""<from>To<to>"" marker segment — that transition IS the
// animation; there is no Play(). Pick a transition the artwork actually draws: the built-in
// glyphs render nothing between Normal and PointerOver even though that segment has duration.
"),

            SampleCard("Set the state directly",
                VStack(12,
                    Border(HStack(AnimatedIcon(picker).Size(32, 32)
                                .Set(icon => XamlAnimatedIcon.SetState(icon, state))))
                        .Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6),
                    Caption($"State: {state} — pick another value to play the transition into it.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// `using static Factories` imports an AnimatedIcon() method that wins simple-name lookup,
// so reaching the WinUI type's static SetState needs an alias:
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

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

            SampleCard("Click a button",
                VStack(12,
                    Button(
                        HStack(8,
                            AnimatedIcon(menuNav).Size(20, 20)
                                .IsHitTestVisible(false)
                                .Set(icon =>
                                {
                                    if (XamlAnimatedIcon.GetState(icon) != menuState)
                                    {
                                        XamlAnimatedIcon.SetState(icon, menuState);
                                    }
                                }),
                            TextBlock(open ? "Close" : "Menu")),
                        () => setOpen(o => !o)),
                    Caption($"Icon state: {menuState} — driven by the menu being open, not by the pointer.")
                        .Foreground(Theme.SecondaryText),
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
// `using static Factories` imports an AnimatedIcon() method that wins simple-name lookup,
// so reaching the WinUI type's static SetState needs an alias:
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

var menuNav = UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
// UseReducer's functional updater reads the live value, so two clicks coalesced into
// one render can't drop a toggle.
var (open, setOpen) = UseReducer(false);
var menuState = open ? ""Pressed"" : ""Normal"";

Button(
    HStack(8,
        AnimatedIcon(menuNav).Size(20, 20)
            .Set(icon =>
            {
                if (XamlAnimatedIcon.GetState(icon) != menuState)
                    XamlAnimatedIcon.SetState(icon, menuState);
            }),
        TextBlock(open ? ""Close"" : ""Menu"")),
    () => setOpen(o => !o))
// Driven by the menu being open, not by the pointer: Normal<->PointerOver renders no
// visible change on this glyph, and a Button's own visual states race the icon's.
")
        ).Margin(36, 24, 36, 36));
    }
}
