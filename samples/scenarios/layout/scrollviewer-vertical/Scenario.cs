// id: scrollviewer-vertical
// intent: scrollable vertical content region
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ScrollView Vertical", width: 400, height: 300);

class App : Component
{
    static Element Row(int index) =>
        Border(
            HStack(12,
                Subtitle($"Item {index}"),
                TextBlock("Scroll to reveal more content.").Foreground(Theme.SecondaryText))
            .Padding(12))
        .Background(Theme.CardBackground)
        .WithBorder(Theme.CardStroke, 1)
        .CornerRadius(8);

    public override Element Render()
    {
        return Border(
            ScrollView(
                VStack(12,
                    Subtitle("Recent activity"),
                    Row(1), Row(2), Row(3), Row(4), Row(5),
                    Row(6), Row(7), Row(8), Row(9), Row(10))
                .Padding(20))
            .Height(220)
        )
        .Background(Theme.SolidBackground);
    }
}
