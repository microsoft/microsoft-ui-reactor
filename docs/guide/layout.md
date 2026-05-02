
# Layout

Reactor provides a small set of layout primitives that compose together to build
any screen structure. Every layout element accepts children and optional
spacing. Once you know the basics here, see [Flex Layout](flex-layout.md) for
more adaptive arrangements.

## VStack and HStack

The two most common layouts. VStack stacks children vertically, HStack
horizontally. The first argument is the pixel spacing between children:

```csharp
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
```

![VStack and HStack](images/layout/vstack-hstack.png)

`VStack(16, ...)` puts 16 pixels between each child, top to bottom.
`HStack(8, ...)` puts 8 pixels between each child, left to right. Omit the
spacing argument for zero spacing: `VStack(child1, child2)`.

Nest them freely — an HStack inside a VStack, VStacks inside HStacks, as deep
as you need.

## Grid

For row-and-column layouts, use `Grid`. Define columns and rows with `GridSize`
helpers, then place children with the `.Grid()` modifier:

```csharp
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
                TextField("", _ => { }, placeholder: "Input...")
                    .Grid(row: 0, column: 1),
                Button("Go").Grid(row: 0, column: 2),
                TextBlock("Status").Grid(row: 1, column: 0),
                TextBlock("Ready").Foreground("#0078D4")
                    .Grid(row: 1, column: 1, columnSpan: 2)
            ).Height(80)
        );
    }
}
```

![Grid layout](images/layout/grid-layout.png)

Use these `GridSize` helpers for tracks:

| Helper | Meaning |
|--------|---------|
| `GridSize.Px(200)` | Fixed 200 pixels |
| `GridSize.Star()` | Proportional — fills available space (weight 1) |
| `GridSize.Star(2)` | Proportional — twice the space of `GridSize.Star()` |
| `GridSize.Auto` | Sizes to content |

Place children with `.Grid(row: 0, column: 1)`. Use `columnSpan` or
`rowSpan` to span multiple cells: `.Grid(row: 1, column: 0, columnSpan: 2)`.

> The older `Grid(["120", "1*", "Auto"], ...)` string-form overload still
> works but is soft-deprecated (`CS0618`). Prefer the typed helpers — they
> catch typos at compile time.

## ScrollView and Border

`ScrollView` wraps content that might overflow. `Border` adds a visual
container with background, corner radius, and padding:

```csharp
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
```

![ScrollView and Border](images/layout/scrollview-border.png)

`ScrollView` takes a single child element. Wrap a `VStack` to scroll a
vertical list. `Border` is purely visual — it renders a rounded rectangle
behind its child and is useful for cards, panels, and grouping.

## Expander and Canvas

`Expander` shows a header and collapses/expands its content on click.
`Canvas` positions children at absolute coordinates:

```csharp
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
```

![Expander and Canvas](images/layout/expander-canvas.png)

`Expander(header, content)` handles the expand/collapse state internally. Pass
`isExpanded: true` to start expanded, or use `onExpandedChanged` to track the
state yourself.

`Canvas` positions children using the `.Set()` modifier to call
`Canvas.SetLeft` and `Canvas.SetTop`. Use it for overlays, diagrams, or any
layout that doesn't follow a stack or grid pattern.

## Responsive Layout

Switch between layouts based on window width. Use a state toggle or
`UseBreakpoint` to swap between HStack and VStack:

```csharp
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
```

![Responsive layout](images/layout/responsive.png)

For real responsive layouts, use [`UseBreakpoint`](hooks.md) `(window, minWidth)` which
returns `true` when the window is at least `minWidth` pixels wide. Pair it
with `If()` to swap layouts:

<!-- ai:lock -->
```csharp
var wide = UseBreakpoint(window, 800);
return If(wide,
    () => HStack(12, panelA, panelB),
    () => VStack(8, panelA, panelB));
```
<!-- /ai:lock -->

`UseWindowSize(window)` returns `(Width, Height)` if you need exact
dimensions for more granular control.

## Alignment and Sizing

Every element supports sizing and alignment modifiers:

<!-- ai:lock -->
```csharp
Text("Centered").HAlign(HorizontalAlignment.Center)
Text("Fixed width").Width(200).Height(40)
VStack(8, items).Margin(24).Padding(16)
```
<!-- /ai:lock -->

| Modifier | Effect |
|----------|--------|
| `.Width(n)` | Fixed width in pixels |
| `.Height(n)` | Fixed height in pixels |
| `.Margin(n)` | Outer spacing (all sides) |
| `.Padding(n)` | Inner spacing (all sides) |
| `.HAlign(alignment)` | Horizontal alignment (Left, Center, Right, Stretch) |
| `.VAlign(alignment)` | Vertical alignment (Top, Center, Bottom, Stretch) |

## Tips

**Start with VStack.** Most screens are a vertical stack of sections. Add
HStack for side-by-side content within those sections.

**Use Grid for forms.** A two-column grid with `[GridSize.Auto, GridSize.Star()]`
columns gives you aligned labels on the left and stretching inputs on the right.

**Wrap long content in ScrollView.** If your VStack might overflow the window,
wrap the whole thing in `ScrollView(VStack(...))`.

**Prefer spacing over margin.** `VStack(12, ...)` gives consistent spacing
between children. Adding `.Margin(12)` to each child individually is harder
to maintain and creates double-spacing at boundaries.

**Use Border for visual grouping.** `Border(content).Background("#F5F5F5")
.CornerRadius(8).Padding(16)` creates a card-like container with no extra
abstraction needed.

## Next Steps

- **[Flex Layout](flex-layout.md)** — Next: flexible box layout for more adaptive UIs
- **[Hooks](hooks.md)** — Previous: UseState, UseReducer, and all the state management hooks
- **[Forms and Input](forms.md)** — Build data entry forms with text fields, checkboxes, and validation
- **[Styling and Theming](styling.md)** — Apply colors, typography, and themes to your layouts
