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

class CounterDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (step, setStep) = UseState(1);

        return VStack(12,
            Heading("Counter"),
            SubHeading($"Current count: {count}"),

            HStack(8,
                Button($"- {step}", () => setCount(count - step)),
                Button("Reset", () => setCount(0)).IsEnabled(!(count == 0)),
                Button($"+ {step}", () => setCount(count + step))
            ),

            HStack(8,
                TextBlock("Step size:"),
                Slider(step, 1, 10, v => setStep((int)v)).Width(200),
                TextBlock($"{step}")
            ),

            // Conditional rendering — shows different messages based on count
            count switch
            {
                0 => TextBlock("Try clicking the buttons!").Foreground(TertiaryText),
                > 0 and < 10 => TextBlock("Going up..."),
                >= 10 and < 50 => TextBlock("Getting bigger!").SemiBold(),
                >= 50 => TextBlock("That's a LOT!").SemiBold(),
                < 0 and > -10 => TextBlock("Going negative..."),
                _ => TextBlock("Way down there!").SemiBold()
            }
        );
    }
}
