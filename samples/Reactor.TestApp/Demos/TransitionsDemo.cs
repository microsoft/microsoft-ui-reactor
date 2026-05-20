using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class TransitionsDemo : Component
{
    public override Element Render()
    {
        var (opacity, setOpacity) = UseState(1.0);
        var (scale, setScale) = UseState(1.0);
        var (xOffset, setXOffset) = UseState(0.0);
        var (bgIndex, setBgIndex) = UseState(0);
        var (showItems, setShowItems) = UseState(true);
        var (itemCount, setItemCount) = UseState(3);
        // Separate state for the combined section so it doesn't bleed into individual demos
        var (comboOpacity, setComboOpacity) = UseState(1.0);
        var (comboScale, setComboScale) = UseState(1.0);
        var (comboBgIndex, setComboBgIndex) = UseState(0);

        // State for layout animation demos
        var (shuffleItems, setShuffleItems) = UseState(new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" });
        var (useWrap, setUseWrap) = UseState(true);
        var (springItems, setSpringItems) = UseState(new[] { "One", "Two", "Three", "Four", "Five" });
        var (useGrid, setUseGrid) = UseState(false);

        string[] colors = ["#4A90D9", "#E8834A", "#50C878", "#9B59B6", "#E74C3C"];
        var currentBg = colors[bgIndex % colors.Length];
        var comboBg = colors[comboBgIndex % colors.Length];

        return ScrollViewer(VStack(16,
            Heading("Transitions"),
            TextBlock("Demonstrates implicit and theme transitions in Reactor.").Foreground(SecondaryText),

            // Section 1: Implicit opacity transition
            SubHeading("Opacity Transition"),
            TextBlock("Drag the slider — the box fades smoothly via an implicit ScalarTransition."),
            HStack(12,
                TextBlock("Opacity:"),
                Slider(opacity, 0, 1, v => setOpacity(v)).Width(200),
                TextBlock($"{opacity:F2}")
            ),
            Border(
                TextBlock("Fade me").SemiBold()
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
                .Size(200, 80).Background("#4A90D9").CornerRadius(8).Padding(16)
                .Opacity(opacity)
                .OpacityTransition(TimeSpan.FromMilliseconds(300)),

            // Section 2: Implicit scale transition
            SubHeading("Scale Transition"),
            TextBlock("Click to toggle scale — animates via an implicit Vector3Transition."),
            HStack(12,
                Button(scale == 1.0 ? "Scale Up" : "Scale Down",
                    () => setScale(scale == 1.0 ? 1.5 : 1.0))
            ),
            Border(
                TextBlock("Scale me").SemiBold()
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
                .Size(200, 80).Background("#E8834A").CornerRadius(8).Padding(16)
                .Set(b => b.Scale = new global::System.Numerics.Vector3((float)scale, (float)scale, 1))
                .ScaleTransition(),

            // Section 3: Implicit translation transition
            SubHeading("Translation Transition"),
            TextBlock("Drag the slider — the box slides via an implicit Vector3Transition."),
            HStack(12,
                TextBlock("X Offset:"),
                Slider(xOffset, -200, 200, v => setXOffset(v)).Width(300),
                TextBlock($"{xOffset:F0}px")
            ),
            Border(
                TextBlock("Slide me").SemiBold()
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
                .Size(200, 80).Background("#50C878").CornerRadius(8).Padding(16)
                .Set(b => b.Translation = new global::System.Numerics.Vector3((float)xOffset, 0, 0))
                .TranslationTransition(),

            // Section 4: Implicit background transition
            // BrushTransition only works on Panel types (Grid, StackPanel), not Border.
            SubHeading("Background Transition"),
            TextBlock("Click to cycle colors — animates via an implicit BrushTransition on a Grid."),
            Button("Next Color", () => setBgIndex(bgIndex + 1)),
            Grid([GridSize.Star()], [GridSize.Px(80)],
                TextBlock(currentBg).SemiBold()
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
                .Width(200).CornerRadius(8)
                .Set(g => g.Background = new SolidColorBrush(ColorFromHex(currentBg)))
                .BackgroundTransition(TimeSpan.FromMilliseconds(500)),

            // Section 5: Theme transitions (ChildrenTransitions)
            SubHeading("Theme Transitions (ChildrenTransitions)"),
            TextBlock("Add/remove items — the panel animates children in/out with theme transitions."),
            HStack(8,
                Button("Add Item", () => setItemCount(Math.Min(itemCount + 1, 8))),
                Button("Remove Item", () => setItemCount(Math.Max(itemCount - 1, 0))),
                Button(showItems ? "Hide All" : "Show All",
                    () => setShowItems(!showItems))
            ),
            showItems
                ? VStack(8,
                    Enumerable.Range(0, itemCount).Select(i =>
                        Border(
                            TextBlock($"Item {i + 1}").SemiBold()
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center)
                        )
                            .Height(50).Background(colors[i % colors.Length])
                            .CornerRadius(8).Padding(12)
                            .WithKey($"transition-item-{i}")
                    ).ToArray()
                ).WithTransitions(
                    new EntranceThemeTransition(),
                    new AddDeleteThemeTransition()
                )
                : TextBlock("(hidden)").Foreground(TertiaryText),

            // Section 6: Combined transitions
            // Uses its own state so toggling doesn't affect the sections above.
            SubHeading("Combined: Opacity + Scale + Background"),
            TextBlock("Multiple implicit transitions on one element — all animate independently."),
            HStack(8,
                Button("Toggle", () =>
                {
                    setComboOpacity(comboOpacity < 0.5 ? 1.0 : 0.3);
                    setComboScale(comboScale == 1.0 ? 1.3 : 1.0);
                    setComboBgIndex(comboBgIndex + 1);
                })
            ),
            Grid([GridSize.Star()], [GridSize.Px(80)],
                TextBlock("All at once").SemiBold()
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
                .Width(200).CornerRadius(8)
                .Opacity(comboOpacity)
                .Set(g =>
                {
                    g.Background = new SolidColorBrush(ColorFromHex(comboBg));
                    g.Scale = new global::System.Numerics.Vector3((float)comboScale, (float)comboScale, 1);
                })
                .OpacityTransition(TimeSpan.FromMilliseconds(400))
                .ScaleTransition()
                .BackgroundTransition(TimeSpan.FromMilliseconds(400)),

            // Section 7: Layout animation — list reorder
            SubHeading("Layout Animation — List Reorder"),
            TextBlock("Keyed items in a VStack. Click Shuffle — items slide to new positions."),
            Button("Shuffle", () => setShuffleItems(shuffleItems.OrderBy(_ => Random.Shared.Next()).ToArray())),
            VStack(8,
                shuffleItems.Select(item =>
                    Border(
                        TextBlock(item).SemiBold()
                            .HAlign(HorizontalAlignment.Center)
                            .VAlign(VerticalAlignment.Center)
                    )
                        .Height(50).Background(ItemColor(item))
                        .CornerRadius(8).Padding(12)
                        .WithKey(item)
                        .LayoutAnimation(TimeSpan.FromMilliseconds(400))
                ).ToArray()
            ),

            // Section 8: Layout animation — flex reflow
            SubHeading("Layout Animation — Flex Reflow"),
            TextBlock("Toggle item width — items reflow within the same FlexPanel, animating to new positions."),
            Button(useWrap ? "Shrink items (fit one row)" : "Expand items (wrap to rows)",
                () => setUseWrap(!useWrap)),
            new FlexElement(
                Enumerable.Range(0, 6).Select(i =>
                    Border(
                        TextBlock($"Item {i}").SemiBold()
                            .HAlign(HorizontalAlignment.Center)
                            .VAlign(VerticalAlignment.Center)
                    )
                        .Background(colors[i % colors.Length]).CornerRadius(8)
                        .Size(useWrap ? 250 : 120, 70)
                        .LayoutAnimation(TimeSpan.FromMilliseconds(400))
                ).ToArray()
            ) { Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 },

            // Section 9: Connected animation — FlexPanel <-> UniformGrid
            SubHeading("Connected Animation — Layout Switch"),
            TextBlock("Switch between FlexPanel (wrapped) and UniformGrid. Items fly from old to new positions via ConnectedAnimationService."),
            Button(useGrid ? "Switch to FlexPanel (wrap)" : "Switch to UniformGrid",
                () => setUseGrid(!useGrid)),
            useGrid
                ? UniformGrid(Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    Enumerable.Range(0, 6).Select(i =>
                        Border(
                            TextBlock($"Item {i}").SemiBold()
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center)
                        )
                            .Background(colors[i % colors.Length]).CornerRadius(8)
                            .Height(70)
                            .ConnectedAnimation($"layout-item-{i}")
                    ).ToArray()
                )
                : new FlexElement(
                    Enumerable.Range(0, 6).Select(i =>
                        Border(
                            TextBlock($"Item {i}").SemiBold()
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center)
                        )
                            .Background(colors[i % colors.Length]).CornerRadius(8)
                            .Size(250, 70)
                            .ConnectedAnimation($"layout-item-{i}")
                    ).ToArray()
                ) { Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 },

            // Section 10: Layout animation — slow reorder
            SubHeading("Layout Animation — Slow Reorder"),
            TextBlock("Same reorder with a slower 800ms animation so you can clearly see items sliding."),
            Button("Shuffle (Slow)", () => setSpringItems(springItems.OrderBy(_ => Random.Shared.Next()).ToArray())),
            VStack(8,
                springItems.Select(item =>
                    Border(
                        TextBlock(item).SemiBold()
                            .HAlign(HorizontalAlignment.Center)
                            .VAlign(VerticalAlignment.Center)
                    )
                        .Height(50).Background(ItemColor(item))
                        .CornerRadius(8).Padding(12)
                        .WithKey($"slow-{item}")
                        .LayoutAnimation(TimeSpan.FromMilliseconds(800))
                ).ToArray()
            ),

            // Section 11: WithAnimation scope
            SubHeading("WithAnimation Scope"),
            TextBlock("Click to animate opacity via WithAnimation scope."),
            Component<WithAnimationScopeDemo>(),

            // Section 12: .Animate() modifier
            SubHeading(".Animate() — Compositor Property Animation"),
            TextBlock("This element has .Animate(Curve.Spring()) — all visual changes animate implicitly."),
            Component<AnimateModifierDemo>(),

            // Section 13: InteractionStates
            SubHeading("InteractionStates — Zero-Reconcile Hover/Press"),
            TextBlock("Hover and press the boxes. No state variables, no re-render."),
            Component<InteractionStatesDemo>(),

            // Section 14: Enter/Exit Transitions
            SubHeading("Enter/Exit Transitions"),
            TextBlock("Toggle to show/hide with Fade + Slide(Bottom) transition."),
            Component<EnterExitTransitionDemo>(),

            // Section 15: Stagger
            SubHeading("Staggered Children"),
            TextBlock("Children with stagger delay — each child's layout animation starts slightly later."),
            Component<StaggerDemo>(),

            // Section 16: Keyframes
            SubHeading("Keyframe Animation"),
            TextBlock("Click to trigger a pulse keyframe animation."),
            Component<KeyframeDemo>()

        )).HorizontalScrollMode(Microsoft.UI.Xaml.Controls.ScrollMode.Disabled)
          .Set(sv => sv.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled);
    }

    /// <summary>Stable color per item identity — doesn't change when position changes.</summary>
    static string ItemColor(string item) => item switch
    {
        "Alpha" or "One" => "#4A90D9",
        "Beta" or "Two" => "#E8834A",
        "Gamma" or "Three" => "#50C878",
        "Delta" or "Four" => "#9B59B6",
        "Epsilon" or "Five" => "#E74C3C",
        _ => "#4A90D9"
    };

    static global::Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return global::Windows.UI.Color.FromArgb(255,
            byte.Parse(hex[0..2], global::System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex[2..4], global::System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex[4..6], global::System.Globalization.NumberStyles.HexNumber));
    }
}

// Helper sub-demos used by TransitionsDemo

class WithAnimationScopeDemo : Component
{
    public override Element Render()
    {
        var (opacity, setOpacity) = UseState(1.0);

        return VStack(8,
            Button(opacity > 0.5 ? "Fade Out (animated)" : "Fade In (animated)", () =>
            {
                Microsoft.UI.Reactor.Animation.AnimationScope.WithAnimation(
                    Microsoft.UI.Reactor.Animation.Curve.Ease(400, Microsoft.UI.Reactor.Animation.Easing.Decelerate), () =>
                    {
                        setOpacity(opacity > 0.5 ? 0.2 : 1.0);
                    });
            }),
            Border(TextBlock("WithAnimation target").SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Size(200, 60).Background("#4A90D9").CornerRadius(8).Padding(12)
                .Opacity(opacity)
        );
    }
}

class AnimateModifierDemo : Component
{
    public override Element Render()
    {
        var (active, setActive) = UseState(false);

        return VStack(8,
            Button(active ? "Reset" : "Animate", () => setActive(!active)),
            Border(TextBlock(".Animate()").SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Size(200, 60).Background("#E8834A").CornerRadius(8).Padding(12)
                .Opacity(active ? 0.5 : 1.0)
                .Animate(Microsoft.UI.Reactor.Animation.Curve.Spring(0.65f))
        );
    }
}

class InteractionStatesDemo : Component
{
    public override Element Render()
    {
        return HStack(12,
            Border(TextBlock("Hover me").SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Size(150, 60).Background("#50C878").CornerRadius(8).Padding(12)
                .InteractionStates(states => states
                    .PointerOver(opacity: 0.85f, scale: 1.05f)
                    .Pressed(scale: 0.95f, opacity: 0.7f)),
            Border(TextBlock("Press me").SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Size(150, 60).Background("#9B59B6").CornerRadius(8).Padding(12)
                .InteractionStates(states => states
                    .PointerOver(scale: 1.03f)
                    .Pressed(scale: 0.97f, opacity: 0.8f),
                    curve: Microsoft.UI.Reactor.Animation.Curve.Spring(0.5f))
        );
    }
}

class EnterExitTransitionDemo : Component
{
    public override Element Render()
    {
        var (visible, setVisible) = UseState(true);

        return VStack(8,
            Button(visible ? "Hide (exit)" : "Show (enter)", () => setVisible(!visible)),
            visible
                ? Border(TextBlock("I fade + slide").SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                    .Size(200, 60).Background("#E74C3C").CornerRadius(8).Padding(12)
                    .Transition(Microsoft.UI.Reactor.Animation.Transition.Fade + Microsoft.UI.Reactor.Animation.Transition.Slide(Microsoft.UI.Reactor.Animation.Edge.Bottom))
                : (Element)TextBlock("(element removed)")
        );
    }
}

class StaggerDemo : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState(new[] { "A", "B", "C", "D", "E" });

        return VStack(8,
            Button("Shuffle", () => setItems(items.OrderBy(_ => Random.Shared.Next()).ToArray())),
            VStack(4,
                items.Select(item =>
                    Border(TextBlock(item).SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                        .Height(40).Background("#4A90D9").CornerRadius(4).Padding(8)
                        .WithKey(item)
                        .LayoutAnimation()
                ).ToArray()
            ).Stagger(TimeSpan.FromMilliseconds(40))
        );
    }
}

class KeyframeDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(8,
            Button("Pulse!", () => setCount(count + 1)),
            Border(TextBlock("Keyframes").SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Size(200, 60).Background("#9B59B6").CornerRadius(8).Padding(12)
                .Keyframes("pulse", count, kf => kf
                    .Duration(600)
                    .At(0.0f, scale: global::System.Numerics.Vector3.One)
                    .At(0.4f, scale: new global::System.Numerics.Vector3(1.2f, 1.2f, 1f), easing: Microsoft.UI.Reactor.Animation.Easing.Decelerate)
                    .At(0.7f, scale: new global::System.Numerics.Vector3(0.95f, 0.95f, 1f))
                    .At(1.0f, scale: global::System.Numerics.Vector3.One, easing: Microsoft.UI.Reactor.Animation.Easing.Accelerate))
        );
    }
}
