// id: localized-text
// intent: localized text display using LocaleProvider
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Localized Text", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return VStack(
            TextBlock("LocaleProvider sets locale context for its subtree."),
            LocaleProvider("en-US", TextBlock("en-US: Hello from Reactor.")),
            LocaleProvider("ar-SA", TextBlock("ar-SA: مرحبا من Reactor.")));
    }
}
