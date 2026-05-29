// id: heading-subhead-caption
// intent: demonstrate the WinUI 3 type ramp (Heading, Subtitle, Caption)
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Type Ramp", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return VStack(
            Title("Title"),
            Heading("Heading"),
            SubHeading("SubHeading"),
            Subtitle("Subtitle"),
            Body("Body"),
            BodyLarge("BodyLarge"),
            BodyStrong("BodyStrong"),
            Caption("Caption"));
    }
}
