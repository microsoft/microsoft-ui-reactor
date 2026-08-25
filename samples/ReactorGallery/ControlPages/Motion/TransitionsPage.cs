using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Motion;

class TransitionsPage : Component
{
    public override Element Render() =>
        PageContent("Transitions",
            "Implicit transitions animate a property whenever its value changes. You never start them: "
            + "set the target with Opacity, Scale, or Translation, attach the matching transition modifier, "
            + "and the next render animates from the old value to the new one. All three run on the "
            + "compositor, so they keep going even while the UI thread is busy. LayoutAnimation is the odd "
            + "one out — it animates position changes that the layout pass produced, not a value you set.",

            Component<OpacitySample>(),
            Component<ScaleSample>(),
            Component<TranslationSample>(),
            Component<LayoutSample>());

    static Element Stage(Element content, double height = 120) =>
        Border(content)
            .Background(Theme.SubtleFill)
            .CornerRadius(8)
            .Padding(16)
            .Width(340)
            .Height(height)
            .HAlign(HorizontalAlignment.Left);

    // ── Opacity ──────────────────────────────────────────────────────

    sealed class OpacitySample : Component
    {
        public override Element Render()
        {
            var (visible, setVisible) = UseState(true);
            var (slow, setSlow) = UseState(false);

            return SampleCard("OpacityTransition",
                VStack(12,
                    Button(visible ? "Fade out" : "Fade in", () => setVisible(!visible)),
                    Stage(TextBlock("Opacity is animated, not toggled")
                        .FontSize(18).Bold()
                        .Foreground(Theme.PrimaryText)
                        .TextWrapping(TextWrapping.Wrap)
                        .Opacity(visible ? 1.0 : 0.0)
                        .OpacityTransition(TimeSpan.FromMilliseconds(slow ? 1500 : 400))),
                    Caption("The element stays in the tree and keeps its layout slot the whole time — "
                            + "this fades it, it does not remove it.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap)),
                sourceCode: @"
var (visible, setVisible) = UseState(true);

TextBlock(""Opacity is animated, not toggled"")
    .Opacity(visible ? 1.0 : 0.0)
    .OpacityTransition(TimeSpan.FromMilliseconds(400))
// Omit the TimeSpan for the 300ms default. Fading to 0 leaves the element in the tree
// and still occupying its layout slot -- use .Transition(...) if you want it to leave.
",
                OptionPanel(
                    TextBlock("Duration"),
                    ToggleSwitch(slow, setSlow, onContent: "1500ms", offContent: "400ms")));
        }
    }

    // ── Scale ────────────────────────────────────────────────────────

    sealed class ScaleSample : Component
    {
        public override Element Render()
        {
            var (enlarged, setEnlarged) = UseState(false);

            return SampleCard("ScaleTransition",
                VStack(12,
                    Button(enlarged ? "Shrink" : "Enlarge", () => setEnlarged(!enlarged)),
                    Stage(Border(TextBlock("Scales from its centre")
                                .FontSize(16).Bold()
                                .Foreground(Theme.PrimaryText))
                            .Padding(12)
                            .CornerRadius(8)
                            .Background(Theme.LayerFill)
                            .HAlign(HorizontalAlignment.Center)
                            .VAlign(VerticalAlignment.Center)
                            .Scale(enlarged ? 1.4f : 1.0f)
                            .ScaleTransition()),
                    Caption("Scale is a render transform, so it does not re-run layout — neighbours "
                            + "stay put and the element can overlap them.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap)),
                sourceCode: @"
var (enlarged, setEnlarged) = UseState(false);

Border(TextBlock(""Scales from its centre""))
    .Scale(enlarged ? 1.4f : 1.0f)
    .ScaleTransition()
// Scale is a compositor transform, not a layout change: siblings do not reflow, so a
// growing element overlaps them rather than pushing them aside.
");
        }
    }

    // ── Translation ──────────────────────────────────────────────────

    sealed class TranslationSample : Component
    {
        public override Element Render()
        {
            var (moved, setMoved) = UseState(false);

            return SampleCard("TranslationTransition",
                VStack(12,
                    Button(moved ? "Slide back" : "Slide right", () => setMoved(!moved)),
                    Stage(TextBlock("Slides horizontally")
                        .FontSize(16).Bold()
                        .Foreground(Theme.PrimaryText)
                        .Translation(moved ? 150f : 0f, 0f, 0f)
                        .TranslationTransition()),
                    Caption("Translation offsets the element from wherever layout put it. Its layout "
                            + "slot never moves, which is what makes it cheap.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap)),
                sourceCode: @"
var (moved, setMoved) = UseState(false);

TextBlock(""Slides horizontally"")
    .Translation(moved ? 150f : 0f, 0f, 0f)
    .TranslationTransition()
// Translation is an offset from the layout position, in device-independent pixels.
// The element's layout slot does not move, so nothing around it reflows.
");
        }
    }

    // ── Layout animation ─────────────────────────────────────────────

    sealed class LayoutSample : Component
    {
        // Hoisted out of Render: a collection expression inline in UseState is rebuilt every
        // render even though only the first one is ever read (REACTOR_HOOKS_013).
        static readonly IReadOnlyList<string> Seed = ["Beta", "Gamma", "Delta"];

        public override Element Render()
        {
            var (items, setItems) = UseState(Seed);
            var (spring, setSpring) = UseState(false);
            var (n, setN) = UseState(0);

            // Inserting at the FRONT is what makes this sample honest: appending to the end
            // moves no existing row, so nothing would animate and the card would look
            // identical with the modifier deleted.
            void AddFirst()
            {
                setN(n + 1);
                setItems(items.Prepend($"Item {n + 1}").ToList());
            }

            void RemoveFirst()
            {
                if (items.Count > 0)
                    setItems(items.Skip(1).ToList());
            }

            return SampleCard("LayoutAnimation",
                VStack(12,
                    HStack(8,
                        Button("Add to top", AddFirst),
                        Button("Remove top", RemoveFirst)),
                    Border(VStack(6, items.Select(i =>
                            (Element)ApplyLayoutAnimation(
                                Border(TextBlock(i).Foreground(Theme.PrimaryText))
                                    .Background(Theme.LayerFill)
                                    .CornerRadius(6)
                                    .Padding(horizontal: 12, vertical: 8)
                                    .HAlign(HorizontalAlignment.Stretch),
                                spring)
                                // Identity across the reorder — without a key the reconciler
                                // would reuse row 0's control for a different item and there
                                // would be no movement to animate.
                                .WithKey($"layout-{i}")).ToArray()))
                        .Background(Theme.SubtleFill)
                        .CornerRadius(8)
                        .Padding(12)
                        .Width(340)
                        .HAlign(HorizontalAlignment.Left),
                    Caption("Rows are added at the top so the existing ones actually have somewhere to "
                            + "move. Appending to the end would animate nothing.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap)),
                sourceCode: @"
var (items, setItems) = UseState<IReadOnlyList<string>>([""Beta"", ""Gamma"", ""Delta""]);

VStack(6, items.Select(i =>
    Border(TextBlock(i))
        .LayoutAnimation()       // animate position changes layout produced
        .WithKey($""layout-{i}"")  // identity across the reorder
).ToArray())

// .LayoutAnimation() animates the OFFSET a layout pass assigned, so something has to
// actually move: insert at the front, not the end. .SpringLayoutAnimation(dampingRatio,
// period) swaps the easing for a spring. .WithKey is not optional here -- without it the
// reconciler reuses row 0's control for whatever is now first and nothing travels.
",
                OptionPanel(
                    TextBlock("Easing"),
                    ToggleSwitch(spring, setSpring, onContent: "Spring", offContent: "Ease")));
        }

        // Position changes the layout pass produced, animated. The two modifiers are
        // mutually exclusive on one element, so the toggle picks between them rather
        // than layering a second one on.
        static BorderElement ApplyLayoutAnimation(BorderElement row, bool spring) =>
            spring
                ? row.SpringLayoutAnimation(dampingRatio: 0.6f, period: 0.08f)
                : row.LayoutAnimation();
    }
}
