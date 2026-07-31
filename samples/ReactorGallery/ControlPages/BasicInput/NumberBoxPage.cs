using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class NumberBoxPage: Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState(0.0);
        var (spinValue, setSpinValue) = UseState(0.0);
        var (rangeValue, setRangeValue) = UseState(50.0);

        return ScrollView(VStack(16,
            PageHeader("NumberBox", "A text control for entering numeric values with validation and optional spin buttons."),

            SampleCard("Basic NumberBox",
                VStack(8,
                    NumberBox(value, v => setValue(v), "Enter a number"),
                    TextBlock($"Value: {value}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
NumberBox(value, v => setValue(v), ""Enter a number"")
"),

            SampleCard("NumberBox with Spin Buttons",
                NumberBox(spinValue, v => setSpinValue(v), "Quantity").SpinButtons(),
                sourceCode: @"
NumberBox(spinValue, v => setSpinValue(v), ""Quantity"").SpinButtons()
"),

            SampleCard("NumberBox with Range",
                VStack(8,
                    NumberBox(rangeValue, v => setRangeValue(v), "Percentage").Range(0, 100).SpinButtons(),
                    TextBlock($"Value: {rangeValue}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
NumberBox(rangeValue, v => setRangeValue(v), ""Percentage"").Range(0, 100).SpinButtons()
")
        ).Margin(36, 24, 36, 36));
    }
}
