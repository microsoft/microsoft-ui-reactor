using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class RadioButtonsPage : Component
{
    public override Element Render()
    {
        var (sizeIndex, setSizeIndex) = UseState(1);
        var (deliveryIndex, setDeliveryIndex) = UseState(0);
        var sizes = new[] { "Small", "Medium", "Large" };
        var deliveryOptions = new[] { "Standard", "Two-day", "Overnight", "Pick up in store" };

        return ScrollView(VStack(16,
            PageHeader("RadioButtons", "RadioButtons let people select one option from a related group."),

            SampleCard("Choose a size",
                VStack(8,
                    RadioButtons(sizes, sizeIndex, index => setSizeIndex(index)),
                    TextBlock($"Selected size: {sizes.ElementAt(sizeIndex)}").Foreground(Theme.SecondaryText)),
                sourceCode: @"var sizes = new[] { ""Small"", ""Medium"", ""Large"" };

RadioButtons(sizes, sizeIndex, index => setSizeIndex(index))
TextBlock($""Selected size: {sizes.ElementAt(sizeIndex)}"")"),

            SampleCard("RadioButtons with a header",
                VStack(8,
                    RadioButtons(deliveryOptions, deliveryIndex, index => setDeliveryIndex(index))
                        .Set(rb => rb.Header = "Choose one"),
                    TextBlock($"Delivery: {deliveryOptions.ElementAt(deliveryIndex)}").Foreground(Theme.SecondaryText)),
                sourceCode: @"var deliveryOptions = new[]
{
    ""Standard"", ""Two-day"", ""Overnight"", ""Pick up in store""
};

RadioButtons(deliveryOptions, deliveryIndex, index => setDeliveryIndex(index))
    .Set(rb => rb.Header = ""Choose one"")")
        ).Margin(36, 24, 36, 36));
    }
}
