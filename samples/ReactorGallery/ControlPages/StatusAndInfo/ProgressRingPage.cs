using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.StatusAndInfo;

class ProgressRingPage : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState(60.0);
        var (isActive, setIsActive) = UseState(true);

        return ScrollView(
            VStack(16,
                PageHeader("ProgressRing",
                    "A circular indicator that shows ongoing progress."),

                SampleCard("Determinate ProgressRing",
                    VStack(8,
                        ProgressRing(value).Width(60).Height(60),
                        TextBlock($"Progress: {value:F0}%").Foreground(Theme.SecondaryText),
                        Slider(value, 0, 100, v => setValue(v)).Width(300)
                    ),
                    @"ProgressRing(value).Width(60).Height(60)",
                    options: OptionPanel(
                        Slider(value, 0, 100, v => setValue(v))
                    )),

                SampleCard("Indeterminate ProgressRing",
                    VStack(8,
                        ProgressRing().IsActive(isActive).Width(60).Height(60),
                        ToggleSwitch(isActive, b => setIsActive(b),
                            onContent: "Active", offContent: "Inactive")
                    ),
                    @"ProgressRing().IsActive(true)")
            ).Margin(36, 24, 36, 36)
        );
    }
}
