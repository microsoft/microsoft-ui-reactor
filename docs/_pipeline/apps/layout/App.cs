using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<LayoutApp>("Layout Demo", width: 700, height: 650
#if DEBUG
    , preview: true
#endif
);

// <snippet:vstack-hstack>
class StackDemo : Component
{
    public override Element Render()
    {
        return VStack(16,
            SubHeading("VStack and HStack"),
            VStack(4,
                TextBlock("VStack: items top to bottom"),
                TextBlock("Item A"), TextBlock("Item B"), TextBlock("Item C")
            ),
            HStack(8,
                TextBlock("HStack:"),
                Button("One"), Button("Two"), Button("Three")
            )
        );
    }
}
// </snippet:vstack-hstack>

// <snippet:grid-layout>
class GridDemo : Component
{
    public override Element Render()
    {
        return VStack(8,
            SubHeading("Grid"),
            Grid(
                columns: [GridSize.Px(120), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto, GridSize.Auto],
                TextBlock("Label").Bold().Grid(row: 0, column: 0),
                TextBox("", _ => { }, placeholderText: "Input...")
                    .Grid(row: 0, column: 1),
                Button("Go").Grid(row: 0, column: 2),
                TextBlock("Status").Grid(row: 1, column: 0),
                TextBlock("Ready").Foreground("#0078D4")
                    .Grid(row: 1, column: 1, columnSpan: 2)
            ).Height(80)
        );
    }
}
// </snippet:grid-layout>

// <snippet:card>
class CardDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Card"),
            Card(
                VStack(8,
                    TextBlock("Recent activity").SemiBold(),
                    TextBlock("3 new messages, 2 mentions")
                        .Foreground(Theme.SecondaryText)
                )
            ).Width(240)
        );
    }
}
// </snippet:card>

// <snippet:type-ramp>
class TypeRampDemo : Component
{
    public override Element Render()
    {
        return VStack(8,
            Title("Quarterly results"),
            Subtitle("Q3 2026 highlights"),
            BodyLarge("Revenue grew 18% year over year."),
            BodyStrong("Net income reached an all-time high."),
            Body("Full breakdown on the following pages.")
        ).Padding(24);
    }
}
// </snippet:type-ramp>

// <snippet:scrollview-border>
class ScrollBorderDemo : Component
{
    public override Element Render()
    {
        return VStack(8,
            SubHeading("ScrollView and Border"),
            Border(
                ScrollView(
                    VStack(4,
                        ForEach(
                            Enumerable.Range(1, 20),
                            i => TextBlock($"Scrollable item {i}"))
                    ).Padding(8)
                ).Height(120)
            ).CornerRadius(4).Background("#F5F5F5")
        );
    }
}
// </snippet:scrollview-border>

// <snippet:expander-canvas>
class ExpanderCanvasDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Expander"),
            Expander("Click to expand", VStack(8,
                TextBlock("Hidden content revealed!"),
                TextBlock("Expanders are great for optional details.")
            )),
            SubHeading("Canvas"),
            Border(
                Canvas(
                    TextBlock("Top-left").Set(c => {
                        Microsoft.UI.Xaml.Controls.Canvas.SetLeft((UIElement)c, 10);
                        Microsoft.UI.Xaml.Controls.Canvas.SetTop((UIElement)c, 10);
                    }),
                    TextBlock("Center").Set(c => {
                        Microsoft.UI.Xaml.Controls.Canvas.SetLeft((UIElement)c, 120);
                        Microsoft.UI.Xaml.Controls.Canvas.SetTop((UIElement)c, 40);
                    })
                ).Height(90).Width(300)
            ).Background("#F5F5F5").CornerRadius(4)
        );
    }
}
// </snippet:expander-canvas>

// <snippet:responsive>
class ResponsiveDemo : Component
{
    public override Element Render()
    {
        var (wide, setWide) = UseState(true);

        var content = new Element[]
        {
            Border(TextBlock("Panel A").Padding(16))
                .Background("#E3F2FD").CornerRadius(4),
            Border(TextBlock("Panel B").Padding(16))
                .Background("#FFF3E0").CornerRadius(4),
            Border(TextBlock("Panel C").Padding(16))
                .Background("#E8F5E9").CornerRadius(4),
        };

        return VStack(8,
            SubHeading("Responsive Layout"),
            HStack(8,
                TextBlock("Simulate wide screen:"),
                ToggleSwitch(wide, setWide)
            ),
            If(wide,
                () => HStack(12, content),
                () => VStack(8, content))
        );
    }
}
// </snippet:responsive>

// <snippet:app-shell>
// App shell scaffold: title bar + sidebar + content using a single Grid
// declaration. The two-column / three-row layout is the canonical
// shape for an admin-style app and replaces hand-rolled nested
// HStacks/VStacks.
class AppShellExample : Component
{
    public override Element Render()
    {
        return Grid(
            columns: [GridSize.Px(220), GridSize.Star()],
            rows:    [GridSize.Px(44),  GridSize.Star(), GridSize.Px(28)],
            // Top bar — spans both columns.
            Border(TextBlock("My App").Bold().Padding(12))
                .Background(Theme.LayerFill)
                .Grid(row: 0, column: 0, columnSpan: 2),
            // Sidebar.
            Border(VStack(4,
                    TextBlock("Dashboard"),
                    TextBlock("Reports"),
                    TextBlock("Settings"))
                .Padding(12))
                .Background(Theme.CardBackground)
                .Grid(row: 1, column: 0),
            // Main content.
            ScrollView(VStack(8,
                    Title("Welcome"),
                    Body("Stack -> Grid for the shell, VStack inside the panes.")))
                .Padding(16)
                .Grid(row: 1, column: 1),
            // Status bar.
            TextBlock("Ready").Padding(6)
                .Grid(row: 2, column: 0, columnSpan: 2)
        ).Width(560).Height(360);
    }
}
// </snippet:app-shell>

// <snippet:auto-grid>
// Auto-grid: WrapGrid wraps a sequence of items into columns once a
// row fills, no manual row/column placement. Picks "max 4 per row".
class AutoGridExample : Component
{
    public override Element Render()
    {
        return WrapGrid(maxRowsOrColumns: 4,
            children: Enumerable.Range(1, 11)
                .Select(i =>
                    Border(TextBlock($"Tile {i}").Padding(12))
                        .Background(Theme.CardBackground)
                        .CornerRadius(6)
                        .Width(110).Height(60))
                .Cast<Element?>()
                .ToArray()
        ).Padding(24);
    }
}
// </snippet:auto-grid>

// <snippet:responsive-switcher>
// Responsive switcher: the same content renders as HStack at wide
// widths, VStack when the window narrows past 480px. UseBreakpoint is
// the canonical hook for this pattern.
class ResponsiveSwitcherExample : Component
{
    public override Element Render()
    {
        var (simulated, setSimulated) = UseState(true);  // toy demo without a window handle

        var panels = new Element[]
        {
            Border(TextBlock("Panel A").Padding(16))
                .Background(Theme.CardBackground).CornerRadius(4),
            Border(TextBlock("Panel B").Padding(16))
                .Background(Theme.CardBackground).CornerRadius(4),
            Border(TextBlock("Panel C").Padding(16))
                .Background(Theme.CardBackground).CornerRadius(4),
        };

        return VStack(8,
            HStack(8,
                TextBlock("Simulate wide window:"),
                ToggleSwitch(simulated, setSimulated)),
            If(simulated,
                () => HStack(12, panels),
                () => VStack(8, panels))
        ).Padding(24);
    }
}
// </snippet:responsive-switcher>

// <snippet:dont-deep-stack>
// BAD — deeply nested VStack/HStack hierarchies obscure intent and
// produce a layout cost that Grid would absorb in one measure pass.
class DontDeepStack : Component
{
    public override Element Render()
    {
        return HStack(8,
            VStack(4,
                TextBlock("Label").Bold(),
                TextBlock("Sublabel").Foreground(Theme.SecondaryText)),
            VStack(0,
                HStack(4, TextBlock("First:"), TextBlock("Value 1")),
                HStack(4, TextBlock("Second:"), TextBlock("Value 2")),
                HStack(4, TextBlock("Third:"), TextBlock("Value 3")))
        ).Padding(24);
    }
}
// </snippet:dont-deep-stack>

// <snippet:do-grid-for-forms>
// GOOD — same shape as a 2-column Grid. Labels in column 0 are auto-
// sized to their content; column 1 stretches.
class DoGridForForms : Component
{
    public override Element Render()
    {
        return Grid(
            columns: [GridSize.Auto, GridSize.Star()],
            rows:    [GridSize.Auto, GridSize.Auto, GridSize.Auto],
            TextBlock("First:").Margin(4).Grid(row: 0, column: 0),
            TextBlock("Value 1").Margin(4).Grid(row: 0, column: 1),
            TextBlock("Second:").Margin(4).Grid(row: 1, column: 0),
            TextBlock("Value 2").Margin(4).Grid(row: 1, column: 1),
            TextBlock("Third:").Margin(4).Grid(row: 2, column: 0),
            TextBlock("Value 3").Margin(4).Grid(row: 2, column: 1)
        ).Padding(24).Width(300);
    }
}
// </snippet:do-grid-for-forms>

class LayoutApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Layout Primitives"),
                Component<StackDemo>(),
                Component<GridDemo>(),
                Component<CardDemo>(),
                Component<TypeRampDemo>(),
                Component<ScrollBorderDemo>(),
                Component<ExpanderCanvasDemo>(),
                Component<ResponsiveDemo>(),
                Component<AppShellExample>(),
                Component<AutoGridExample>(),
                Component<ResponsiveSwitcherExample>(),
                Component<DoGridForForms>()
            ).Padding(24)
        );
    }
}
