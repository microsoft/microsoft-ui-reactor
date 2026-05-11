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

class FlyoutDemo : Component
{
    public override Element Render()
    {
        var (tick, updateTick) = UseReducer(0);
        var (color, setColor) = UseState("Red");

        // Timer ticks every second to test dynamic flyout content.
        UseEffect(() =>
        {
            var timer = new Microsoft.UI.Xaml.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => updateTick(t => t + 1);
            timer.Start();
            return () => timer.Stop();
        }, []);

        var colors = new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple" };
        var currentColorHex = color switch
        {
            "Red" => "#e57373",
            "Orange" => "#ffb74d",
            "Yellow" => "#fff176",
            "Green" => "#81c784",
            "Blue" => "#64b5f6",
            "Purple" => "#ba68c8",
            _ => "#e0e0e0"
        };

        return ScrollView(VStack(16,
            Heading("Flyout Attachments"),
            TextBlock("Tests declarative .WithFlyout(), .WithContextFlyout(), and .WithToolTip(Element) modifiers."),
            TextBlock($"Timer tick: {tick} (flyout content updates every second)").Foreground(SecondaryText),

            // 1. ContentFlyout on a Button via .WithFlyout()
            SubHeading("1. Button with ContentFlyout (dynamic content)"),
            TextBlock("Click the button to see a flyout with a live-updating counter."),
            Button("Open Flyout", null)
                .WithFlyout(ContentFlyout(
                    VStack(12,
                        TextBlock("Dynamic Flyout Content").SemiBold(),
                        TextBlock($"Timer tick: {tick}").FontSize(20),
                        Border(
                            TextBlock($"Elapsed: {tick} seconds")
                        ).CornerRadius(4).Background(SubtleFill).Padding(horizontal: 12, vertical: 8),
                        HStack(8,
                            Enumerable.Range(0, Math.Min(tick % 10, 8)).Select(i =>
                                (Element)Border(Empty())
                                    .Background(colors[i % colors.Length] switch
                                    {
                                        "Red" => "#e57373",
                                        "Orange" => "#ffb74d",
                                        "Yellow" => "#fff176",
                                        "Green" => "#81c784",
                                        "Blue" => "#64b5f6",
                                        _ => "#ba68c8"
                                    })
                                    .CornerRadius(4)
                                    .Size(24, 24)
                            ).ToArray()
                        )
                    ),
                    placement: Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
                )),

            // 2. MenuFlyout on DropDownButton
            SubHeading("2. DropDownButton with MenuItems"),
            TextBlock("A DropDownButton with a declarative menu flyout."),
            DropDownButton("Pick a color", flyout:
                MenuItems(
                    MenuItem("Red", () => setColor("Red")),
                    MenuItem("Orange", () => setColor("Orange")),
                    MenuItem("Yellow", () => setColor("Yellow")),
                    MenuSeparator(),
                    MenuItem("Green", () => setColor("Green")),
                    MenuItem("Blue", () => setColor("Blue")),
                    MenuItem("Purple", () => setColor("Purple"))
                )
            ),
            HStack(8,
                TextBlock($"Selected: {color}"),
                Border(Empty()).Background(currentColorHex).CornerRadius(4).Size(24, 24)
            ),

            // 3. SplitButton with ContentFlyout
            SubHeading("3. SplitButton with ContentFlyout"),
            TextBlock("SplitButton with a declarative color grid flyout."),
            SplitButton($"Apply {color}", () => { /* primary action */ }, flyout:
                ContentFlyout(
                    VStack(8,
                        TextBlock("Pick a color:").SemiBold(),
                        HStack(4,
                            colors.Select(c =>
                            {
                                var hex = c switch
                                {
                                    "Red" => "#e57373",
                                    "Orange" => "#ffb74d",
                                    "Yellow" => "#fff176",
                                    "Green" => "#81c784",
                                    "Blue" => "#64b5f6",
                                    "Purple" => "#ba68c8",
                                    _ => "#e0e0e0"
                                };
                                return (Element)Button("", () => setColor(c))
                                    .Set(b =>
                                    {
                                        b.Content = new Microsoft.UI.Xaml.Controls.Border
                                        {
                                            Width = 32, Height = 32,
                                            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                                Microsoft.UI.Colors.Transparent),
                                            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4)
                                        };
                                        ((Microsoft.UI.Xaml.Controls.Border)b.Content).Background =
                                            BrushHelper.Parse(hex);
                                        b.Padding = new Microsoft.UI.Xaml.Thickness(0);
                                        b.MinWidth = 0;
                                        b.MinHeight = 0;
                                    });
                            }).ToArray()
                        )
                    ),
                    placement: Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
                )
            ),

            // 4. ContextFlyout on any element
            SubHeading("4. ContextFlyout (right-click menu)"),
            TextBlock("Right-click the box below to see a context menu."),
            Border(
                VStack(8,
                    TextBlock("Right-click me!").SemiBold(),
                    TextBlock($"Color: {color} | Tick: {tick}")
                )
            ).CornerRadius(8).Background(SubtleFill).Padding(24)
             .WithContextFlyout(MenuItems(
                MenuItem("Reset color", () => setColor("Red")),
                MenuItem("Reset timer", () => updateTick(_ => 0)),
                MenuSeparator(),
                MenuItem("Set Blue", () => setColor("Blue")),
                MenuItem("Set Green", () => setColor("Green"))
             )),

            // 5. Rich ToolTip
            SubHeading("5. Rich ToolTip (Element content)"),
            TextBlock("Hover over the button below for a rich tooltip with dynamic content."),
            Button("Hover me", null)
                .WithToolTip(
                    VStack(8,
                        TextBlock("Rich ToolTip").SemiBold(),
                        TextBlock($"Current color: {color}"),
                        TextBlock($"Timer: {tick}s"),
                        Border(Empty())
                            .Background(currentColorHex)
                            .CornerRadius(4)
                            .Size(80, 16)
                    )
                )
        ));
    }
}
