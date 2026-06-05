using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

class OptionalSnapBackDemo : Component
{
    public override Element Render()
    {
        var (lastAttempt, setLastAttempt) = UseState("Move the slider away from 5.");
        var (tick, bump) = UseReducer(false);

        return ScrollView(
            VStack(16,
                Heading("Optional<T> snap-back recipe"),
                Body("The Slider is always rendered with Value = 5.0 (the implicit conversion to Optional<double> means \"force-assert this value\"). When the user moves it, the callback bumps a reducer to force a render and Reactor asserts 5.0 again."),
                Slider(5.0, 0, 10, value =>
                    {
                        setLastAttempt($"User tried {value:0.##}; snapped back to 5.");
                        bump(flag => !flag);
                    })
                    .Width(360)
                    .Margin(tick ? 0 : 1),
                TextBlock(lastAttempt),
                Caption("Manual smoke: drag the thumb away from 5; it returns to 5 after the change callback."))
            .Spacing(12));
    }
}
