// id: grid-spans
// intent: grid with row and column spans
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Grid Spans", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            (Grid(
                new[] { GridSize.Star(2), GridSize.Star(), GridSize.Star() },
                new[] { GridSize.Auto, GridSize.Star() },
                Card(VStack(8,
                    Subtitle("Overview"),
                    TextBlock("This tile spans two columns across the top.").Foreground(Theme.SecondaryText)))
                    .Grid(row: 0, column: 0, columnSpan: 2),
                Card(VStack(8,
                    Subtitle("Alerts"),
                    Caption("7 items need attention.").Foreground(Theme.SecondaryText)))
                    .Grid(row: 0, column: 2, rowSpan: 2),
                Card(TextBlock("Traffic")).Grid(row: 1, column: 0),
                Card(TextBlock("Tasks")).Grid(row: 1, column: 1)) with
            {
                ColumnSpacing = 12,
                RowSpacing = 12,
            })
            .Padding(20)
        )
        .Background(Theme.SolidBackground);
    }
}
