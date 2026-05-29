// id: hstack-basic
// intent: horizontal shrink-wrap stack with spacing
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("HStack Basic", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            HStack(8,
                TextBlock("Name").Width(48),
                TextField("Ada", _ => { }, placeholder: "Display name").Width(180),
                Button("Save"))
            .Padding(20)
        )
        .Background(Theme.SolidBackground);
    }
}
