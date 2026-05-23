using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using XamlAlignment = Microsoft.UI.Xaml.HorizontalAlignment;

namespace WinFormsInterop.Sample;

/// <summary>
/// A Reactor component displayed inside a WinForms window via XAML Island.
/// Demonstrates Reactor's declarative UI, hooks-based state, and WinUI rendering.
/// </summary>
class SampleReactorComponent : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (name, setName) = UseState("World");
        var (sliderValue, setSliderValue) = UseState(50.0);

        // Grid stretches to fill the island and provides a themed background.
        // DesktopWindowXamlSource doesn't stretch content or provide a background
        // like a WinUI Window does — hosted components should fill their container.
        return Grid([GridSize.Star()], [GridSize.Star()],
          VStack(
            // ── Header ─────────────────────────────────────
            SubHeading("Reactor Component (via XAML Island)").Margin(0, 0, 0, 4),

            TextBlock("This Reactor/WinUI component is rendered inside a XAML Island hosted in a WinForms window.")
                .Foreground(SecondaryText)
                .Margin(0, 0, 0, 20),

            // ── Counter ────────────────────────────────────
            HStack(
                Button("-", () => setCount(count - 1)).AutomationName("Decrement"),
                TextBlock($"  {count}  ")
                    .FontSize(20)
                    .VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                Button("+", () => setCount(count + 1)).AutomationName("Increment")
            ).Margin(0, 0, 0, 16),

            // ── Text input ─────────────────────────────────
            HStack(
                TextBlock("Name: ")
                    .VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                TextBox(name, setName)
                    .Width(200)
            ).Margin(0, 0, 0, 8),

            TextBlock($"Hello, {name}! (count={count})")
                .Margin(0, 0, 0, 16),

            // ── Slider ─────────────────────────────────────
            Slider(sliderValue, onValueChanged: setSliderValue)
                .Width(300)
                .Margin(0, 0, 0, 4),

            Caption($"Slider: {sliderValue:F0}%")
                .Foreground(SecondaryText)
                .Margin(0, 0, 0, 20),

            // ── Visual proof of WinUI rendering ────────────
            Caption("WinUI CornerRadius + accent colors:")
                .Foreground(SecondaryText)
                .Margin(0, 0, 0, 6),

            HStack(
                Enumerable.Range(0, 5).Select(i =>
                    Border(
                        TextBlock($"{i + 1}")
                            .Foreground(Theme.Ref("TextOnAccentFillColorPrimaryBrush"))
                            .HAlign(XamlAlignment.Center)
                            .VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center)
                    )
                    .CornerRadius(8)
                    .Background(i == count % 5 ? Accent : ControlFill)
                    .Size(50, 50)
                    .Margin(4)
                    .WithKey(i.ToString())
                ).ToArray()
            ),

            Caption("These rounded boxes use WinUI rendering — not possible in WinForms.")
                .Foreground(TertiaryText)
                .Margin(0, 8, 0, 0)
          ).Padding(24)
        ).Background(SolidBackground);
    }
}
