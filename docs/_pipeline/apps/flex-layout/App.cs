using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<FlexLayoutApp>("Flex Layout", width: 700, height: 600
#if DEBUG
    , preview: true
#endif
);

// <snippet:flex-direction>
class FlexDirectionDemo : Component
{
    public override Element Render()
    {
        return VStack(16,
            SubHeading("Row (default)"),
            FlexRow(
                TextBlock("A").Padding(12).Background("#e0e0ff"),
                TextBlock("B").Padding(12).Background("#ffe0e0"),
                TextBlock("C").Padding(12).Background("#e0ffe0")
            ) with { ColumnGap = 8 },

            SubHeading("Column"),
            FlexColumn(
                TextBlock("A").Padding(12).Background("#e0e0ff"),
                TextBlock("B").Padding(12).Background("#ffe0e0"),
                TextBlock("C").Padding(12).Background("#e0ffe0")
            ) with { RowGap = 8 }
        ).Padding(24);
    }
}
// </snippet:flex-direction>

// <snippet:justify-align>
class JustifyAlignDemo : Component
{
    public override Element Render()
    {
        return VStack(16,
            SubHeading("JustifyContent: SpaceBetween"),
            FlexRow(
                TextBlock("Left").Padding(8).Background("#e0e0ff"),
                TextBlock("Center").Padding(8).Background("#ffe0e0"),
                TextBlock("Right").Padding(8).Background("#e0ffe0")
            ) with { JustifyContent = FlexJustify.SpaceBetween },

            SubHeading("AlignItems: Center"),
            FlexRow(
                TextBlock("Short").Padding(8).Background("#e0e0ff"),
                TextBlock("Tall\nItem").Padding(8).Background("#ffe0e0"),
                TextBlock("Med").Padding(8).Background("#e0ffe0")
            ) with {
                AlignItems = FlexAlign.Center,
                ColumnGap = 8
            }
        ).Padding(24).Height(300);
    }
}
// </snippet:justify-align>

// <snippet:wrap-gap>
class WrapGapDemo : Component
{
    public override Element Render()
    {
        var tags = new[] {
            "C#", "WinUI", "Reactor", ".NET", "XAML",
            "Flex", "Layout", "Desktop", "Native"
        };

        return VStack(12,
            SubHeading("Wrapping Tags"),
            FlexRow(
                tags.Select(tag =>
                    TextBlock(tag)
                        .Padding(horizontal: 6, vertical: 12)
                        .Background("#e8e8e8")
                        .CornerRadius(12)
                ).ToArray()
            ) with {
                Wrap = FlexWrap.Wrap,
                ColumnGap = 8,
                RowGap = 8
            }
        ).Padding(24);
    }
}
// </snippet:wrap-gap>

// <snippet:grow-shrink>
class GrowShrinkDemo : Component
{
    public override Element Render()
    {
        return VStack(16,
            SubHeading("Grow: sidebar + content"),
            FlexRow(
                TextBlock("Sidebar")
                    .Padding(16).Background("#e0e0ff")
                    .Flex(basis: 200, shrink: 0),
                TextBlock("Main content area")
                    .Padding(16).Background("#f0f0f0")
                    .Flex(grow: 1)
            ) with { ColumnGap = 8 },

            SubHeading("Equal columns"),
            FlexRow(
                TextBlock("Column 1").Padding(16).Background("#ffe0e0").Flex(grow: 1),
                TextBlock("Column 2").Padding(16).Background("#e0ffe0").Flex(grow: 1),
                TextBlock("Column 3").Padding(16).Background("#e0e0ff").Flex(grow: 1)
            ) with { ColumnGap = 8 }
        ).Padding(24);
    }
}
// </snippet:grow-shrink>

// <snippet:toolbar>
class ToolbarDemo : Component
{
    public override Element Render()
    {
        var (selected, setSelected) = UseState("Home");

        return VStack(0,
            FlexRow(
                TextBlock("MyApp").Bold().Flex(shrink: 0),
                Empty().Flex(grow: 1),
                Button("Home", () => setSelected("Home")),
                Button("Settings", () => setSelected("Settings")),
                Button("About", () => setSelected("About"))
            ) with {
                AlignItems = FlexAlign.Center,
                ColumnGap = 8,
                FlexPadding = new Thickness(16, 8, 16, 8)
            },
            TextBlock($"Current page: {selected}")
                .Padding(24).FontSize(18)
        );
    }
}
// </snippet:toolbar>

// <snippet:flex-vs-stack>
class FlexVsStackDemo : Component
{
    public override Element Render()
    {
        return VStack(16,
            SubHeading("HStack (fixed spacing)"),
            HStack(8,
                Button("A"), Button("B"), Button("C")
            ),

            SubHeading("FlexRow (justify + align)"),
            FlexRow(
                Button("A"), Button("B"), Button("C")
            ) with {
                JustifyContent = FlexJustify.SpaceEvenly,
                AlignItems = FlexAlign.Center
            }
        ).Padding(24);
    }
}
// </snippet:flex-vs-stack>

// <snippet:app-shell>
class AppShellDemo : Component
{
    public override Element Render()
    {
        return FlexRow(
            // Sidebar — fixed 220px, never shrinks below it.
            VStack(8,
                TextBlock("Inbox").Padding(8),
                TextBlock("Drafts").Padding(8),
                TextBlock("Sent").Padding(8)
            ).Background("#f3f3f3")
             .Flex(basis: 220, shrink: 0),

            // Content — explicit basis: 0 + grow: 1 gives a single distribution
            // pass instead of measuring the inner text first.
            VStack(12,
                Heading("Inbox"),
                TextBlock("Three messages, one starred. The sidebar stays 220px wide; this column absorbs every spare pixel.")
            ).Padding(16)
             .Flex(grow: 1, basis: 0)
        ) with { ColumnGap = 1 };
    }
}
// </snippet:app-shell>

// <snippet:responsive-nav>
class ResponsiveNavDemo : Component
{
    public override Element Render()
    {
        // Wrap kicks in when the narrow viewport can no longer fit one row.
        // RowGap and ColumnGap apply between wrapped lines too — no manual margin.
        return FlexRow(
            TextBlock("Home").Padding(8).Background("#e0e0ff"),
            TextBlock("Catalog").Padding(8).Background("#e0e0ff"),
            TextBlock("Pricing").Padding(8).Background("#e0e0ff"),
            TextBlock("Docs").Padding(8).Background("#e0e0ff"),
            TextBlock("About").Padding(8).Background("#e0e0ff"),
            TextBlock("Contact").Padding(8).Background("#e0e0ff"),
            TextBlock("Status").Padding(8).Background("#e0e0ff")
        ) with {
            Wrap = FlexWrap.Wrap,
            ColumnGap = 8,
            RowGap = 8,
            AlignItems = FlexAlign.Center
        };
    }
}
// </snippet:responsive-nav>

// <snippet:width-vs-grow-wrong>
class WidthVsGrowWrong : Component
{
    public override Element Render()
    {
        // Don't: .Width(200) sets the WinUI Width, but inside a FlexPanel
        // child sizing is governed by Flex(basis/grow/shrink). The 200 is
        // silently ignored when grow > 0 fills available space.
        return FlexRow(
            TextBlock("Stays 200?")
                .Width(200)              // ignored — grow wins
                .Flex(grow: 1)
                .Background("#ffe0e0")
        ) with { ColumnGap = 8 };
    }
}
// </snippet:width-vs-grow-wrong>

// <snippet:width-vs-grow-right>
class WidthVsGrowRight : Component
{
    public override Element Render()
    {
        // Do: encode the intended size as basis with shrink: 0 — Flex
        // owns the sizing math, so no surprise overrides.
        return FlexRow(
            TextBlock("Exactly 200")
                .Flex(basis: 200, shrink: 0)
                .Background("#e0ffe0")
                .Padding(8)
        ) with { ColumnGap = 8 };
    }
}
// </snippet:width-vs-grow-right>

class FlexLayoutApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Flex Layout"),
                Component<FlexDirectionDemo>(),
                Component<JustifyAlignDemo>(),
                Component<WrapGapDemo>(),
                Component<GrowShrinkDemo>(),
                Component<ToolbarDemo>(),
                Component<FlexVsStackDemo>(),
                Component<AppShellDemo>(),
                Component<ResponsiveNavDemo>(),
                Component<WidthVsGrowRight>()
            ).Padding(24)
        );
    }
}
