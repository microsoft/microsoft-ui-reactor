using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class OneWayClearValueFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var fallbackBrush = new SolidColorBrush(Color.FromArgb(255, 1, 2, 3));
            var assertedBrush = new SolidColorBrush(Color.FromArgb(255, 200, 10, 10));
            var style = new Style(typeof(WinUI.Button));
            style.Setters.Add(new Setter(WinUI.Control.BackgroundProperty, fallbackBrush));

            var button = new WinUI.Button
            {
                Content = "clear-value-target",
                Style = style,
            };
            H.SetContent(button);
            await Harness.Render();

            var entry = new ControlDescriptor<ClearBrushElement, WinUI.Button>()
                .OneWay(e => e.Background, (c, v) => c.Background = v, WinUI.Control.BackgroundProperty)
                .Properties[0];

            var oldElement = new ClearBrushElement(Optional<Brush>.Of(assertedBrush));
            entry.Mount(button, oldElement);
            await Harness.Render();

            H.Check("OneWayClearValue_HasValueWritesLocalBrush", ReferenceEquals(button.Background, assertedBrush));
            H.Check("OneWayClearValue_LocalValuePresentBeforeUnset", ReferenceEquals(button.ReadLocalValue(WinUI.Control.BackgroundProperty), assertedBrush));

            entry.Update(button, oldElement, new ClearBrushElement(Optional<Brush>.Unset));
            await Harness.Render();

            H.Check("OneWayClearValue_UnsetClearsLocalValue", ReferenceEquals(DependencyProperty.UnsetValue, button.ReadLocalValue(WinUI.Control.BackgroundProperty)));
            H.Check("OneWayClearValue_StyleFallbackBrushRestored", ReferenceEquals(button.Background, fallbackBrush));
        }
    }

    private sealed record ClearBrushElement(Optional<Brush> Background = default) : Element;
}
