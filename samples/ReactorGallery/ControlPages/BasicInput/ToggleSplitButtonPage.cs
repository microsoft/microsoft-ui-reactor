using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class ToggleSplitButtonPage : Component
{
    public override Element Render()
    {
        var (bulletsOn, setBulletsOn) = UseState(false);
        var (boldOn, setBoldOn) = UseState(true);
        var bulletItems = new[] { "First item", "Second item", "Third item" };

        return ScrollView(VStack(16,
            PageHeader("ToggleSplitButton", "A ToggleSplitButton combines a two-state button with split-button styling."),

            SampleCard("Bullets toggle",
                VStack(8,
                    ToggleSplitButton("Bullets", bulletsOn, value => setBulletsOn(value)),
                    TextBlock(bulletsOn ? "Bullets are on" : "Bullets are off").Foreground(Theme.SecondaryText),
                    VStack(4,
                        bulletItems.Select(item => TextBlock(bulletsOn ? $"• {item}" : item)).ToArray())),
                sourceCode: @"ToggleSplitButton(""Bullets"", bulletsOn, value => setBulletsOn(value))

TextBlock(bulletsOn ? ""Bullets are on"" : ""Bullets are off"")"),

            SampleCard("Bold text toggle",
                VStack(8,
                    ToggleSplitButton("Bold", boldOn, value => setBoldOn(value)),
                    (boldOn ? TextBlock("Preview text").Bold() : TextBlock("Preview text"))
                        .FontSize(20)
                        .Foreground(Theme.PrimaryText),
                    TextBlock(boldOn ? "The preview is bold." : "The preview is regular.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"ToggleSplitButton(""Bold"", boldOn, value => setBoldOn(value))

(boldOn ? TextBlock(""Preview text"").Bold() : TextBlock(""Preview text""))
    .FontSize(20)")
        ).Margin(36, 24, 36, 36));
    }
}
