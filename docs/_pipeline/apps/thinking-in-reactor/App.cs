using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `thinking-in-reactor.md`. Each step of the worked example lives
// in its own namespace (StaticVersion, WithState, Final) so the page can show
// the same component names evolving step by step.
ReactorApp.Run<ThinkingInReactorApp>("Thinking in Reactor");

class ThinkingInReactorApp : Component
{
    public override Element Render() =>
        ScrollView(
            VStack(24,
                Heading("Thinking in Reactor"),
                Component<ThinkingInReactor.Final.ProductCatalog>(),
                Component<ThinkingInReactor.Concepts.FavoriteButton>(),
                Component<ThinkingInReactor.Concepts.SearchBox>()
            ).Padding(24));
}
