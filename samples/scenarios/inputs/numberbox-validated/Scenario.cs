// id: numberbox-validated
// intent: number input with validation constraints
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("NumberBox Validation", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (age, setAge) = UseState(21.0);
        return VStack(12,
            Heading("NumberBox"),
            (NumberBox(age, setAge) with { Header = "Age", Minimum = 0, Maximum = 120, Description = "Allowed range: 0 to 120" })
                .AutomationName("Age"),
            TextBlock($"Current value: {age:0}"))
            .Margin(16);
    }
}
