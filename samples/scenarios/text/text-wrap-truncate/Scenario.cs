// id: text-wrap-truncate
// intent: text wrapping and trimming with ellipsis
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Wrap and Truncate", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var sample = "A longer sentence shows how text behaves when the available width is smaller than the content.";

        return VStack(
            TextBlock(sample).TextWrapping(TextWrapping.Wrap).MaxLines(2),
            TextBlock(sample)
                .TextWrapping(TextWrapping.Wrap)
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .MaxLines(1));
    }
}
