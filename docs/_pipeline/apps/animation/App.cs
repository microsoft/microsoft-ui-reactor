using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<AnimationApp>("Animation", width: 650, height: 700
#if DEBUG
    , preview: true
#endif
);

// <snippet:opacity-transition>
class OpacityDemo : Component
{
    public override Element Render()
    {
        var (visible, setVisible) = UseState(true);

        return VStack(12,
            SubHeading("Opacity Transition"),
            Button(visible ? "Fade Out" : "Fade In",
                () => setVisible(!visible)),
            TextBlock("This text fades in and out")
                .FontSize(18).Bold()
                .Opacity(visible ? 1.0 : 0.0)
                .OpacityTransition(TimeSpan.FromMilliseconds(500))
        ).Padding(24);
    }
}
// </snippet:opacity-transition>

// <snippet:scale-transition>
class ScaleDemo : Component
{
    public override Element Render()
    {
        var (enlarged, setEnlarged) = UseState(false);

        return VStack(12,
            SubHeading("Scale Transition"),
            Button(enlarged ? "Shrink" : "Enlarge",
                () => setEnlarged(!enlarged)),
            Border(
                TextBlock("Scales up and down").FontSize(18).Bold()
            ).Padding(12)
             .CornerRadius(8)
             .Background("#e8e8e8")
             .ScaleTransition()
        ).Padding(24);
    }
}
// </snippet:scale-transition>

// <snippet:translation-transition>
class TranslationDemo : Component
{
    public override Element Render()
    {
        var (moved, setMoved) = UseState(false);

        return VStack(12,
            SubHeading("Translation Transition"),
            Button(moved ? "Slide Back" : "Slide Right",
                () => setMoved(!moved)),
            TextBlock("Slides horizontally")
                .FontSize(18).Bold()
                .Translation(moved ? 120f : 0f, 0f, 0f)
                .TranslationTransition()
        ).Padding(24);
    }
}
// </snippet:translation-transition>

// <snippet:background-transition>
class BackgroundDemo : Component
{
    public override Element Render()
    {
        var (warm, setWarm) = UseState(false);

        return VStack(12,
            SubHeading("Background Transition"),
            Button(warm ? "Cool Colors" : "Warm Colors",
                () => setWarm(!warm)),
            VStack(8,
                TextBlock("Background animates between colors")
                    .Foreground("#ffffff").Bold()
            ).Padding(16)
             .CornerRadius(8)
             .Background(warm ? "#da3b01" : "#0078d4")
             .BackgroundTransition(TimeSpan.FromMilliseconds(600))
        ).Padding(24);
    }
}
// </snippet:background-transition>

// <snippet:combined-transitions>
class CombinedDemo : Component
{
    public override Element Render()
    {
        var (active, setActive) = UseState(false);

        return VStack(12,
            SubHeading("Combined Transitions"),
            Button(active ? "Reset" : "Animate",
                () => setActive(!active)),
            Border(
                TextBlock("All at once").FontSize(16).Bold()
                    .Foreground("#ffffff")
            ).Padding(16)
             .CornerRadius(8)
             .Background("#7b2ab5")
             .Opacity(active ? 1.0 : 0.4)
             .Translation(active ? 40f : 0f, 0f, 0f)
             .OpacityTransition(TimeSpan.FromMilliseconds(400))
             .TranslationTransition()
        ).Padding(24);
    }
}
// </snippet:combined-transitions>

// <snippet:layout-animation>
class LayoutAnimationDemo : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(
            new List<string> { "Apple", "Banana", "Cherry" });
        var nextId = UseRef(3);

        return VStack(12,
            SubHeading("Layout Animation"),
            HStack(8,
                Button("Add Item", () => {
                    nextId.Current++;
                    updateItems(l => [.. l, $"Item {nextId.Current}"]);
                }),
                Button("Remove Last", () => updateItems(l =>
                    l.Count > 0 ? l.Take(l.Count - 1).ToList() : l))
            ),
            VStack(4, items.Select(item =>
                TextBlock(item).Padding(horizontal: 8, vertical: 12).Background("#f0f0f0")
                    .CornerRadius(4).LayoutAnimation()
                    .WithKey($"item-{item}")
            ).ToArray())
        ).Padding(24);
    }
}
// </snippet:layout-animation>

// <snippet:connected-animation>
class ConnectedAnimationDemo : Component
{
    public override Element Render()
    {
        var (selected, setSelected) = UseState<string?>(null);

        if (selected is not null)
            return VStack(12,
                Button("Back to list", () => setSelected(null)),
                TextBlock(selected)
                    .FontSize(28).Bold()
                    .ConnectedAnimation($"title-{selected}")
            ).Padding(24);

        var items = new[] { "Photos", "Music", "Videos" };
        return VStack(12,
            SubHeading("Connected Animation"),
            VStack(4,
                items.Select(item =>
                    Button(item, () => setSelected(item))
                        .ConnectedAnimation($"title-{item}")
                ).ToArray()
            )
        ).Padding(24);
    }
}
// </snippet:connected-animation>

// <snippet:with-animation>
class WithAnimationDemo : Component
{
    public override Element Render()
    {
        var (opacity, setOpacity) = UseState(1.0);

        return VStack(12,
            SubHeading("WithAnimation Scope"),
            Button(opacity > 0.5 ? "Fade Out" : "Fade In", () =>
            {
                Microsoft.UI.Reactor.Animation.AnimationScope.WithAnimation(
                    Microsoft.UI.Reactor.Animation.Curve.Ease(300, Microsoft.UI.Reactor.Animation.Easing.Decelerate), () =>
                    {
                        setOpacity(opacity > 0.5 ? 0.2 : 1.0);
                    });
            }),
            TextBlock("Compositor-animated via WithAnimation scope")
                .FontSize(18).Bold()
                .Opacity(opacity)
        ).Padding(24);
    }
}
// </snippet:with-animation>

// <snippet:animate-modifier>
class AnimateDemo : Component
{
    public override Element Render()
    {
        var (active, setActive) = UseState(false);

        return VStack(12,
            SubHeading(".Animate() Modifier"),
            Button(active ? "Reset" : "Animate", () => setActive(!active)),
            Border(
                TextBlock("Spring-animated").FontSize(18).Bold()
            ).Padding(12).CornerRadius(8).Background("#e8e8e8")
             .Opacity(active ? 0.5 : 1.0)
             .Animate(Microsoft.UI.Reactor.Animation.Curve.Spring(0.65f))
        ).Padding(24);
    }
}
// </snippet:animate-modifier>

// <snippet:interaction-states>
class InteractionStatesDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("InteractionStates"),
            TextBlock("Hover and press — zero reconcile, compositor-driven."),
            HStack(12,
                Border(
                    TextBlock("Hover me").FontSize(16).Bold()
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                ).Padding(16).CornerRadius(8).Size(150, 60).Background("#50C878")
                 .InteractionStates(s => s
                    .PointerOver(opacity: 0.85f, scale: 1.05f)
                    .Pressed(scale: 0.95f, opacity: 0.7f)),
                Border(
                    TextBlock("Press me").FontSize(16).Bold()
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                ).Padding(16).CornerRadius(8).Size(150, 60).Background("#9B59B6")
                 .InteractionStates(s => s
                    .PointerOver(scale: 1.03f)
                    .Pressed(scale: 0.97f, opacity: 0.8f),
                    curve: Microsoft.UI.Reactor.Animation.Curve.Spring(0.5f))
            )
        ).Padding(24);
    }
}
// </snippet:interaction-states>

// <snippet:enter-exit-transition>
class TransitionDemo : Component
{
    public override Element Render()
    {
        var (visible, setVisible) = UseState(true);

        return VStack(12,
            SubHeading("Enter/Exit Transition"),
            Button(visible ? "Hide" : "Show", () => setVisible(!visible)),
            visible
                ? Border(
                    TextBlock("Fade + Slide").FontSize(16).Bold()
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                ).Padding(12).CornerRadius(8).Size(200, 60).Background("#E74C3C")
                 .Transition(Microsoft.UI.Reactor.Animation.Transition.Fade + Microsoft.UI.Reactor.Animation.Transition.Slide(Microsoft.UI.Reactor.Animation.Edge.Bottom))
                : (Element)TextBlock("(removed from tree)")
        ).Padding(24);
    }
}
// </snippet:enter-exit-transition>

// <snippet:stagger>
class StaggerDemo : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState(new[] { "One", "Two", "Three", "Four", "Five" });

        return VStack(12,
            SubHeading("Staggered Animation"),
            Button("Shuffle", () => setItems(items.OrderBy(_ => Random.Shared.Next()).ToArray())),
            VStack(4, items.Select(item =>
                TextBlock(item).Padding(horizontal: 8, vertical: 12).Background("#f0f0f0")
                    .CornerRadius(4).LayoutAnimation()
                    .WithKey(item)
            ).ToArray()).Stagger(TimeSpan.FromMilliseconds(40))
        ).Padding(24);
    }
}
// </snippet:stagger>

// <snippet:keyframes>
class KeyframeDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            SubHeading("Keyframe Animation"),
            Button("Pulse!", () => setCount(count + 1)),
            Border(
                TextBlock("Pulse target").FontSize(16).Bold()
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
            ).Padding(12).CornerRadius(8).Size(200, 60).Background("#9B59B6")
             .Keyframes("pulse", count, kf => kf
                .Duration(600)
                .At(0.0f, scale: global::System.Numerics.Vector3.One)
                .At(0.4f, scale: new global::System.Numerics.Vector3(1.3f, 1.3f, 1f), easing: Microsoft.UI.Reactor.Animation.Easing.Decelerate)
                .At(0.7f, scale: new global::System.Numerics.Vector3(0.95f, 0.95f, 1f))
                .At(1.0f, scale: global::System.Numerics.Vector3.One, easing: Microsoft.UI.Reactor.Animation.Easing.Accelerate))
        ).Padding(24);
    }
}
// </snippet:keyframes>

// <snippet:choreography>
class ChoreographyDemo : Component
{
    public override Element Render()
    {
        var (phase, setPhase) = UseState(0);

        return VStack(12,
            SubHeading("Choreography (WithAnimationAsync)"),
            Button("Run Sequence", async () =>
            {
                await Microsoft.UI.Reactor.Animation.AnimationScope.WithAnimationAsync(
                    Microsoft.UI.Reactor.Animation.Curve.Ease(200), () => setPhase(1));
                await Microsoft.UI.Reactor.Animation.AnimationScope.WithAnimationAsync(
                    Microsoft.UI.Reactor.Animation.Curve.Spring(0.7f), () => setPhase(2));
            }),
            TextBlock($"Phase: {phase}").FontSize(18).Bold()
                .Opacity(phase == 0 ? 1.0 : phase == 1 ? 0.3 : 1.0)
        ).Padding(24);
    }
}
// </snippet:choreography>

// Main app
class AnimationApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Animation"),
                Component<OpacityDemo>(),
                Component<ScaleDemo>(),
                Component<TranslationDemo>(),
                Component<BackgroundDemo>(),
                Component<CombinedDemo>(),
                Component<LayoutAnimationDemo>(),
                Component<ConnectedAnimationDemo>(),
                Component<WithAnimationDemo>(),
                Component<AnimateDemo>(),
                Component<InteractionStatesDemo>(),
                Component<TransitionDemo>(),
                Component<StaggerDemo>(),
                Component<KeyframeDemo>(),
                Component<ChoreographyDemo>()
            ).Padding(24)
        );
    }
}
