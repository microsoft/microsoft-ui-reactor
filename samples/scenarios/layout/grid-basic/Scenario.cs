// id: grid-basic
// intent: 2D grid with column and row sizes
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Grid Basic", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            (Grid(
                new[] { GridSize.Auto, GridSize.Star() },
                new[] { GridSize.Auto, GridSize.Auto, GridSize.Star() },
                TextBlock("Name").Grid(row: 0, column: 0).Foreground(Theme.SecondaryText),
                TextField("Ada Lovelace", _ => { }).Grid(row: 0, column: 1),
                TextBlock("Team").Grid(row: 1, column: 0).Foreground(Theme.SecondaryText),
                TextField("Layout systems", _ => { }).Grid(row: 1, column: 1),
                Border(TextBlock("The bottom row expands because the second column and third row use star sizing.").Padding(12))
                    .Background(Theme.CardBackground)
                    .WithBorder(Theme.CardStroke, 1)
                    .CornerRadius(8)
                    .Grid(row: 2, column: 0, columnSpan: 2)) with
            {
                ColumnSpacing = 12,
                RowSpacing = 12,
            })
            .Padding(20)
        )
        .Background(Theme.SolidBackground);
    }
}
