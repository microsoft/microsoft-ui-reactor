
# Flex Layout

Reactor includes a full CSS Flexbox implementation powered by Yoga. Use `FlexRow`
and `FlexColumn` when you need alignment control, wrapping, or proportional
sizing that [`VStack` and `HStack`](layout.md) cannot express.

Add `using Reactor.Flex;` to access the enum types: `FlexDirection`, `FlexJustify`,
`FlexAlign`, and `FlexWrap`.

## Direction

`FlexRow` lays children out horizontally. `FlexColumn` stacks them vertically.
Set gap with the `ColumnGap` and `RowGap` properties:

```csharp
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
```

![Row and column direction](images/flex-layout/flex-direction.png)

These behave like [`HStack` and `VStack`](layout.md) at first glance, but flex
containers unlock justification, alignment, wrapping, and grow/shrink
proportions that stack panels do not support.

## Justify and Align

`JustifyContent` distributes children along the main axis. `AlignItems`
positions them on the cross axis. Set these using record `with { }` syntax:

```csharp
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
```

![Justify and align demo](images/flex-layout/justify-align.png)

| JustifyContent value | Effect |
|---------------------|--------|
| `FlexStart` | Pack to start (default) |
| `Center` | Center along main axis |
| `FlexEnd` | Pack to end |
| `SpaceBetween` | Equal space between items |
| `SpaceAround` | Equal space around items |
| `SpaceEvenly` | Equal space between and at edges |

| AlignItems value | Effect |
|-----------------|--------|
| `Stretch` | Fill cross axis (default) |
| `FlexStart` | Align to cross-axis start |
| `Center` | Center on cross axis |
| `FlexEnd` | Align to cross-axis end |
| `Baseline` | Align text baselines |

## Wrapping and Gap

When items overflow the container, set `Wrap = FlexWrap.Wrap` to flow them
onto the next line. Use `ColumnGap` and `RowGap` to control spacing:

```csharp
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
```

![Wrapping tags with gap](images/flex-layout/wrap-gap.png)

This pattern is ideal for tag clouds, chip lists, or any content where the
number of items varies and you want them to reflow naturally. For rendering
dynamic lists of items, see [Collections](collections.md).

## Grow and Shrink

The `.Flex()` modifier on child elements controls how they share available
space:

- **grow** — how much extra space this child absorbs (default 0, meaning
  the child stays at its natural size)
- **shrink** — how much this child gives up when space is tight (default 1)
- **basis** — the starting size in pixels before grow/shrink apply

```csharp
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
```

![Grow and shrink layout](images/flex-layout/grow-shrink.png)

The sidebar example uses `basis: 200` with `shrink: 0` to create a fixed-width
sidebar, while the content area uses `grow: 1` to fill the rest. The equal
columns example gives each child `grow: 1` so they share space evenly.

## Practical Example: Toolbar

A toolbar with a left-aligned title and right-aligned buttons is a classic
flex layout. Use `Empty().Flex(grow: 1)` as a spacer:

```csharp
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
```

![Toolbar layout](images/flex-layout/toolbar.png)

The `Empty()` element with `grow: 1` acts as a flexible spacer, pushing the
buttons to the right side while the title stays left.

## Flex vs VStack/HStack

Use `VStack`/`HStack` when you need simple stacking with fixed spacing. Use
`FlexRow`/`FlexColumn` when you need justification, alignment, wrapping, or
proportional sizing:

```csharp
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
```

![Flex vs stack comparison](images/flex-layout/flex-vs-stack.png)

| Feature | VStack/HStack | FlexRow/FlexColumn |
|---------|--------------|-------------------|
| Fixed spacing | Yes | Yes (via gap) |
| Justify content | No | Yes |
| Align items | No | Yes |
| Wrap | No | Yes |
| Grow/shrink | No | Yes |
| Proportional sizing | No | Yes |

## Tips

**Start with VStack/HStack.** They cover most layouts and produce simpler
element trees. Reach for Flex only when you need its extra capabilities.

**Use `ColumnGap` and `RowGap` instead of margins.** Gap applies spacing
between children automatically without extra margin on the first or last child.

**Set `shrink: 0` on fixed-width items.** Without it, a sidebar or icon column
can collapse when the window shrinks. Pair it with `basis` to set the exact
size.

**Use `Empty().Flex(grow: 1)` as a spacer.** This is the flex equivalent of a
spring — it absorbs all remaining space and pushes siblings to opposite edges.

**Remember `with { }` syntax for container props.** FlexElement is a C# record,
so you set `JustifyContent`, `AlignItems`, `Wrap`, and gap using
`FlexRow(...) with { JustifyContent = FlexJustify.Center }`.

## Next Steps

- **[Layout](layout.md)** — VStack, HStack, Grid, and other core layout containers
- **[Forms and Input](forms.md)** — controlled input controls, validation, and form composition
- **[Collections](collections.md)** — render dynamic lists and grids with virtualization
- **[Styling and Theming](styling.md)** — apply visual styles, colors, and themes to your layouts
