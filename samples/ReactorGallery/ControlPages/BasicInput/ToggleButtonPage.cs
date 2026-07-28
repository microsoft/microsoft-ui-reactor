using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class ToggleButtonPage: Component
{
    public override Element Render()
    {
        var (isChecked, setIsChecked) = UseState(false);
        var (isChecked2, setIsChecked2) = UseState(true);
        var (triState, setTriState) = UseState<bool?>(null);

        return ScrollView(VStack(16,
            PageHeader("ToggleButton", "A button that can be toggled between two states."),

            SampleCard("Basic ToggleButton",
                VStack(8,
                    ToggleButton("Mute", isChecked, v => setIsChecked(v)),
                    TextBlock($"Toggled: {isChecked}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
ToggleButton(""Mute"", isChecked, v => setIsChecked(v))
"),

            SampleCard("ToggleButton with State Display",
                VStack(8,
                    ToggleButton(isChecked2 ? "ON" : "OFF", isChecked2, v => setIsChecked2(v)),
                    TextBlock(isChecked2 ? "Feature is enabled" : "Feature is disabled").Foreground(Theme.SecondaryText)),
                sourceCode: @"
ToggleButton(isChecked2 ? ""ON"" : ""OFF"", isChecked2, v => setIsChecked2(v))
"),

            SampleCard("Three-State ToggleButton",
                    VStack(8,
                        ThreeStateToggleButton("Review", triState, v => setTriState(v)),
                        TextBlock($"State: {(triState == null ? "Indeterminate" : triState.ToString())}").Foreground(Theme.SecondaryText)),
                    sourceCode: @"
ThreeStateToggleButton(""Review"", triState, v => setTriState(v))
")
        ).Margin(36, 24, 36, 36));
    }
}
