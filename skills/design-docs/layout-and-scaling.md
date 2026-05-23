# Layout and Scaling in Reactor

## The 4px Grid

All margins, padding, and sizing values should be multiples of 4: 4, 8, 12, 16, 20, 24, 32, 40, 48, etc.

Odd values (3, 5, 7, 11, 15) cause blurry rendering at fractional DPI scales (125%, 150%, 175%).

```csharp
// Correct: multiples of 4
VStack(8,
    TextBlock("Item 1"),
    TextBlock("Item 2")
).Padding(16).Margin(12)

// Wrong: odd values
VStack(5,
    TextBlock("Item 1"),
    TextBlock("Item 2")
).Padding(15).Margin(7)
```

## Corner Radius

Windows 11 defines two standard corner radii:

| Token | Value | Use Case |
|-------|-------|----------|
| `ControlCornerRadius` | 4px | Controls, buttons, cards |
| `OverlayCornerRadius` | 8px | Flyouts, dialogs, popups |

```csharp
// Control corner radius (4px)
Border(child).CornerRadius(4)
Button("Action").CornerRadius(4)

// Overlay corner radius (8px)
Border(flyoutContent).CornerRadius(8)

// Selective rounding (top corners only for panels)
Border(panelContent).CornerRadius(8, 8, 0, 0)

// Wrong: non-standard radius
Border(child).CornerRadius(3)   // Not in the design system
Border(child).CornerRadius(6)   // Not in the design system
```

**Figma caveat:** Design tools sometimes show non-standard values. Always map to 4 or 8 in code.

## Layout Containers

**Default for linear layout:** `FlexColumn` / `FlexRow`. They implement CSS
Flexbox semantics via Yoga, so `grow`, `shrink`, `basis`, `gap`, `wrap`,
`justify-content`, and `align-items` all behave the way web-trained engineers
and designers expect. `VStack` / `HStack` (StackPanel) remain appropriate when
you specifically want StackPanel's shrink-wrap cross-axis behavior, are
porting existing StackPanel code, or prefer its terser single-arg spacing.

### FlexColumn / FlexRow (CSS Flexbox — preferred for linear layout)

```csharp
FlexColumn(
    Heading("Title"),
    TextBlock("Description"),
    Button("Action", onClick)
) with { RowGap = 8 }

FlexRow(
    Image(icon).Size(24, 24),
    TextBlock("Label"),
    TextBlock("Value").Foreground(Theme.SecondaryText)
) with { ColumnGap = 12, AlignItems = FlexAlign.Center }
```

### VStack / HStack (StackPanel)

Linear vertical or horizontal layout with optional spacing. Good fit when
StackPanel's shrink-wrap cross-axis is what you want.

```csharp
VStack(8,
    Heading("Title"),
    TextBlock("Description"),
    Button("Action", onClick)
)

HStack(12,
    Image(icon).Size(24, 24),
    TextBlock("Label"),
    TextBlock("Value").Foreground(Theme.SecondaryText)
)
```

### Grid (Row/Column)

Explicit row/column definitions with child positioning.

```csharp
Grid(
    columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
    rows: [GridSize.Auto, GridSize.Star()],

    TextBlock("Label").Grid(row: 0, column: 0),
    TextBox(value, setValue).Grid(row: 0, column: 1),
    Button("Go", onClick).Grid(row: 0, column: 2),

    ScrollView(
        VStack(8, content)
    ).Grid(row: 1, column: 0, columnSpan: 3))
```

**Track helpers (spec 033 §1):**
- `GridSize.Star()` — flexible, takes remaining space (weight 1)
- `GridSize.Star(2)` — flexible, twice the weight
- `GridSize.Auto` — sizes to content
- `GridSize.Px(200)` — fixed 200px

The legacy string-form overload (`Grid(["Auto", "*"], …)`) still compiles but
is soft-deprecated (`CS0618`).

### Flex (CSS Flexbox)

Modern flexible layout via Yoga engine. Best for proportional, wrapping, and complex responsive layouts.

```csharp
// Responsive row with grow
Flex(
    TextBlock("Fixed").Flex(shrink: 0, basis: 100),
    Border(child).Flex(grow: 1),
    Button("Action").Flex(shrink: 0)
) with
{
    JustifyContent = FlexJustify.SpaceBetween,
    AlignItems = FlexAlign.Center
}

// Wrapping grid of cards
Flex(
    cards.Select(card =>
        CardComponent(card)
            .Flex(basis: 300, grow: 1)
            .WithKey(card.Id)
    ).ToArray()
) with
{
    Wrap = FlexWrap.Wrap,
    ColumnGap = 16,
    RowGap = 16
}
```

### Border (Single-Child Container)

For a single child with background, border, padding, or corner radius.

```csharp
// Correct: Border for styled single-child container
Border(
    TextBlock("Card content")
).Padding(16).CornerRadius(4).Background(Theme.CardBackground)

// Wrong: Grid or VStack for single child
Grid([GridSize.Star()], [GridSize.Star()],
    TextBlock("Card content")
).Background(Theme.CardBackground)
```

### ScrollView

Wrap content that may exceed available space. Default to `ScrollView()`
— the modern `Microsoft.UI.Xaml.Controls.ScrollView`. Reach for
`ScrollViewer()` only when you need a `Control`-shaped API
(`HorizontalContentAlignment`, parallax animations, the
`ScrollViewer.SetXxx` attached properties on templated parents, or the
`IsIntermediate` view-changed flag).

```csharp
ScrollView(VStack(8, longContent))
```

**Configuration:**
- Use `Auto` scrollbar visibility (the default) — scrollbar appears only when content overflows.
- Headers and action bars should remain outside the scroller — only the content area scrolls.

```csharp
// Correct: fixed header, scrolling content
VStack(
    Heading("Page Title").Padding(16, 16, 16, 8),
    ScrollView(VStack(8, contentItems)))
```

## Sizing

### Prefer Min/Max Over Fixed

```csharp
// Correct: flexible sizing
Button("Submit").MinHeight(40)
VStack(content).MinWidth(200).MaxWidth(600)
TextBox(value, setValue).MinHeight(32)

// Wrong: fixed sizing clips at larger text scales
Button("Submit").Height(32)
TextBox(value, setValue).Height(30).Width(200)
```

### No Fixed Widths on Buttons

Let content determine width, or set `MinWidth`:

```csharp
// Correct
Button("Save", onSave)
Button("Save", onSave).MinWidth(120)

// Wrong
Button("Save", onSave).Width(100)  // Clips long localized strings
```

### No Fixed Heights on Text Containers

Fixed height clips text at larger text scaling settings:

```csharp
// Correct
Border(TextBlock(message)).MinHeight(40).Padding(8)

// Wrong
Border(TextBlock(message)).Height(40)  // Clips at 200% text scale
```

## Alignment

```csharp
// Horizontal alignment
TextBlock("Centered").HAlign(HorizontalAlignment.Center)
Button("Right").HAlign(HorizontalAlignment.Right)
TextBlock("Stretch").HAlign(HorizontalAlignment.Stretch)

// Vertical alignment
TextBlock("Middle").VAlign(VerticalAlignment.Center)

// Center both axes
TextBlock("Centered").Center()
```

### Mixed-Control Rows

When placing different controls side by side (e.g., toggle + text), vertically center all participants:

```csharp
HStack(8,
    ToggleSwitch(isOn, setOn).VAlign(VerticalAlignment.Center),
    TextBlock("Enable feature").VAlign(VerticalAlignment.Center))
```

## Spacing

Use container spacing, not spacer elements:

```csharp
// Correct: spacing parameter
VStack(8, TextBlock("A"), TextBlock("B"), TextBlock("C"))

// Wrong: spacer element
VStack(
    TextBlock("A"),
    Border(null).Height(8).Opacity(0),
    TextBlock("B"))
```

For Grid, use row/column spacing via `.Set()`:

```csharp
Grid(columns, rows, children).Set(g =>
{
    g.RowSpacing = 8;
    g.ColumnSpacing = 12;
})
```

## Shadows

### ThemeShadow Pattern

```csharp
Border(content)
    .CornerRadius(8)
    .Translation(0, 0, 32)  // Required: elevation makes shadow visible
    .Set(b =>
    {
        b.Shadow = new ThemeShadow();
    })
```

**Rules:**
- `Translation(0, 0, 32)` is required — without it, the shadow is invisible.
- Add 12px padding on the parent to prevent shadow clipping.
- Shadow receivers must be at a lower z-order than the elevated element.
- Use `ThemeShadow` instead of composition drop shadows.

## Text Trimming

`HStack` (StackPanel) and `FlexRow` both give children unbounded main-axis width, so `TextTrimming` never activates inside them. Use a `Grid` with a `GridSize.Star()` column:

```csharp
// Correct: Grid constrains width
Grid(
    columns: [GridSize.Auto, GridSize.Star()],
    rows: [GridSize.Auto],
    Image(avatar).Size(32, 32).Grid(column: 0),
    TextBlock(title)
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .ToolTip(title)  // Show full text on hover when trimmed
        .Grid(column: 1))

// Wrong: TextTrimming never fires in HStack or FlexRow
HStack(8,
    Image(avatar).Size(32, 32),
    TextBlock(title).TextTrimming(TextTrimming.CharacterEllipsis))
```

**Important:** `GridSize.Auto` also sizes to content and prevents trimming — always use `GridSize.Star()` for the column that contains trimmable text. This is a common mistake when multiple columns are present.

### Unnecessary Container Wrappers

Remove wrapper containers (`Border`, `Grid`, `VStack`) that exist only for nesting without contributing layout, styling, or semantic purpose:

```csharp
// Wrong: Grid wrapper serves no purpose
Grid([GridSize.Star()], [GridSize.Star()],
    TextBlock("Hello").Grid(row: 0, column: 0))

// Correct: just the text
TextBlock("Hello")
```

## BiDi / RTL Support

Use logical margin and padding for bidirectional layouts:

```csharp
TextBlock("Label")
    .MarginInlineStart(16)   // Left in LTR, right in RTL
    .PaddingInlineEnd(8)     // Right in LTR, left in RTL
```

## Display Scaling

Test at 100%, 150%, 200%, and 250% scaling. Common issues:
- Blurry edges from odd-numbered sizes (use 4px grid).
- Clipped text from fixed heights.
- Misaligned controls from hardcoded positioning.
- Shadow clipping from insufficient parent padding.

## Design Handoff

When implementing from Figma or design comps:
- Measure at 100% scale factor.
- Map corner radius to system values (4 or 8).
- Map spacing to 4px grid multiples.
- Validate against the running build on the same scale factor.
