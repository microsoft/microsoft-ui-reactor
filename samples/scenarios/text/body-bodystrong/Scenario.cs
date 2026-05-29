// id: body-bodystrong
// intent: body text with emphasis using BodyStrong
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Body and BodyStrong", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return VStack(
            Body("Body is useful for longer explanatory text in a layout."),
            BodyStrong("BodyStrong highlights the sentence you want readers to notice first."),
            Body("Mix them together to keep paragraphs readable while emphasizing key details."));
    }
}
