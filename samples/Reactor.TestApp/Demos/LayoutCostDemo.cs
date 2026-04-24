using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

// Deliberately three sub-Components at very different inflation ratios so the
// layout-cost overlay paints visibly different meter lengths / colors when the
// overlay is toggled on (Dev menu → "Show layout cost overlay").
//   • Simple Text column      → tiny, low inflation.
//   • Button grid             → medium count, medium inflation (templated).
//   • Faux DataGrid (100 rows)→ large count, high inflation.
class LayoutCostDemo : Component
{
    public override Element Render()
    {
        var (rows, setRows) = UseState(100);
        return VStack(16,
            Heading("Layout cost overlay"),
            TextBlock("Toggle via Dev menu → \"Show layout cost overlay\". "
                    + "Each child Component below gets its own badge showing "
                    + "measure+arrange time (top bar) and authored-vs-rendered "
                    + "element count (bottom bar).")
                .Foreground(TertiaryText),

            HStack(8,
                TextBlock("Rows:"),
                Slider(rows, 10, 500, v => setRows((int)v)).Width(260),
                TextBlock($"{rows}")
            ),

            Component<TextColumn>(),
            Component<ButtonGrid>(),
            Component<FakeDataGrid, FakeDataGridProps>(new FakeDataGridProps(rows))
        );
    }
}

file class TextColumn : Component
{
    public override Element Render() =>
        Border(
            VStack(4,
                SubHeading("TextColumn"),
                TextBlock("One Component."),
                TextBlock("A few lines of text."),
                TextBlock("Authored ≈ rendered."),
                TextBlock("Expect green bars.")
            ).Padding(8)
        ).WithBorder("#30C0C0C0", 1).Padding(4);
}

file class ButtonGrid : Component
{
    public override Element Render()
    {
        var items = Enumerable.Range(0, 20).Select(i => Button($"B{i}") as Element).ToArray();
        return Border(
            VStack(4,
                SubHeading("ButtonGrid"),
                TextBlock("20 buttons — medium inflation ratio."),
                WrapGrid(items)
            ).Padding(8)
        ).WithBorder("#30C0C0C0", 1).Padding(4);
    }
}

file record FakeDataGridProps(int Rows);

file class FakeDataGrid : Component<FakeDataGridProps>
{
    public override Element Render()
    {
        var rows = Props?.Rows ?? 100;
        var rowElements = Enumerable.Range(0, rows).Select(i =>
            HStack(12,
                TextBlock($"#{i:D4}").Width(60),
                TextBlock($"Item {i}").Width(120),
                TextBlock((i * 37 % 1000).ToString("N0")).Width(80),
                TextBlock(i % 2 == 0 ? "active" : "idle").Width(60)
            ) as Element).ToArray();

        return Border(
            VStack(4,
                SubHeading("FakeDataGrid"),
                TextBlock($"{rows} rows — high inflation ratio (templated cells)."),
                ScrollView(VStack(2, rowElements)).Height(240)
            ).Padding(8)
        ).WithBorder("#30C0C0C0", 1).Padding(4);
    }
}
