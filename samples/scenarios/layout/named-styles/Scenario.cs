// id: named-styles
// intent: apply theme-aware named styles to buttons, hyperlinks, and InfoBars
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Named Styles", width: 560, height: 520);

class App : Component
{
    public override Element Render() =>
        ScrollView(
            VStack(20,
                Subtitle("Buttons"),
                HStack(8,
                    Button("Default", () => { }),
                    Button("Accent", () => { }).AccentButton(),
                    Button("Subtle", () => { }).SubtleButton(),
                    Button("Text link", () => { }).TextLink()),
                Subtitle("Hyperlinks"),
                HyperlinkButton("Open docs").TextLink(),
                Subtitle("InfoBars"),
                InfoBar("Tip", "You can drag the divider.").Informational(),
                InfoBar("Saved", "Changes written to disk.").Success(),
                InfoBar("Heads up", "Unsaved changes will be discarded.").Warning(),
                InfoBar("Failed", "Couldn't reach the server.").Error())
            .Padding(24));
}
