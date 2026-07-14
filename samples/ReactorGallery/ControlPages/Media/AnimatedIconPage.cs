using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class AnimatedIconPage : Component
{
    static Element Cell(string label, Element icon) =>
        VStack(6,
            Border(icon.Center()).Size(72, 56).Background(Theme.SubtleFill).CornerRadius(6),
            Caption(label).Foreground(Theme.SecondaryText).Center());

    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("AnimatedIcon", "An icon that transitions between states with a vector animation. Ideal inside buttons and expanders."),

            SampleCard("Built-in animated icons",
                HStack(20,
                    Cell("Chevron", AnimatedIcon(new AnimatedChevronDownSmallVisualSource()).Size(32, 32)),
                    Cell("Settings", AnimatedIcon(new AnimatedSettingsVisualSource()).Size(32, 32)),
                    Cell("Nav", AnimatedIcon(new AnimatedGlobalNavigationButtonVisualSource()).Size(32, 32))),
                sourceCode: @"
AnimatedIcon(new AnimatedChevronDownSmallVisualSource()).Size(32, 32)
// AnimatedIcon plays its transition when the host raises a state change
// (e.g. an Expander toggling). Supply your own IRichAnimatedVisualSource
// for custom motion.
"),

            SampleCard("Inside a button",
                Button(
                    HStack(8,
                        AnimatedIcon(new AnimatedGlobalNavigationButtonVisualSource()).Size(20, 20),
                        TextBlock("Menu")),
                    () => { }),
                sourceCode: @"
Button(
    HStack(8,
        AnimatedIcon(new AnimatedGlobalNavigationButtonVisualSource()).Size(20, 20),
        TextBlock(""Menu"")),
    () => { })
")
        ).Margin(36, 24, 36, 36));
    }
}
