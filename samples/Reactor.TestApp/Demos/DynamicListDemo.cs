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

class DynamicListDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(3);
        var (showIndices, setShowIndices) = UseState(true);

        return VStack(12,
            Heading("Dynamic List"),
            TextBlock("Demonstrates conditional and list rendering"),

            HStack(8,
                Button("Remove", () => setCount(Math.Max(0, count - 1))).Disabled(count == 0),
                TextBlock($"{count} items"),
                Button("Add", () => setCount(count + 1))
            ),

            CheckBox(showIndices, setShowIndices, label: "Show indices"),

            // Dynamic list generated from a range
            VStack(4,
                Enumerable.Range(0, count).Select(i =>
                    Border(
                        HStack(8,
                            When(showIndices, () => TextBlock($"#{i + 1}").SemiBold()),
                            TextBlock($"Item {i + 1}"),
                            TextBlock($"(created dynamically)").Foreground(TertiaryText)
                        )
                    ).CornerRadius(4).Background(SubtleFill).Padding(horizontal: 12, vertical: 8)
                ).ToArray()
            ),

            When(count == 0, () => TextBlock("No items. Click Add to create some.").Foreground(TertiaryText)),
            When(count >= 10, () => TextBlock("That's a lot of items!").SemiBold())
        );
    }
}
