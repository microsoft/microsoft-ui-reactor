---
name: reactor-dsl-reference
description: >
  Exhaustive DSL catalog for Reactor — every factory (TextBlock, Button, Grid,
  Flex, NavigationView, TabView, ContentDialog, MenuBar, DataGrid, ...),
  every fluent modifier (.Margin, .Padding, .Background, .Grid, .Flex,
  .Set, ...), and all WinUI enums used by the API. Load this when you
  need to look up a specific control or modifier signature.
---

# Reactor DSL Reference

All factories live on `Microsoft.UI.Reactor.Factories` — use
`using static Microsoft.UI.Reactor.Factories;` to bring them into scope.

## Text

| Factory | Description | Signature |
|---------|-------------|-----------|
| `TextBlock(content)` | Basic text | `string → TextBlockElement` |
| `Heading(content)` | 28px bold | `string → TextBlockElement` |
| `SubHeading(content)` | 20px semi-bold | `string → TextBlockElement` |
| `Caption(content)` | 12px | `string → TextBlockElement` |
| `Title(content)` | WinUI `TitleTextBlockStyle` (28px Semibold) | `string → TextBlockElement` |
| `Subtitle(content)` | WinUI `SubtitleTextBlockStyle` (20px Semibold) | `string → TextBlockElement` |
| `Body(content)` | WinUI `BodyTextBlockStyle` (14px Regular) | `string → TextBlockElement` |
| `BodyStrong(content)` | WinUI `BodyStrongTextBlockStyle` (14px Semibold) | `string → TextBlockElement` |
| `BodyLarge(content)` | WinUI `BodyLargeTextBlockStyle` (18px Regular) | `string → TextBlockElement` |
| `RichTextBlock(text)` | Rich text block | `string → RichTextBlockElement` |

**Implicit conversion:** `string` implicitly converts to `TextBlockElement`, so
`VStack("Hello", "World")` works.

## Buttons

| Factory | Signature |
|---------|-----------|
| `Button(label, onClick?)` | `(string, Action?)` |
| `HyperlinkButton(content, navigateUri?, onClick?)` | `(string, Uri?, Action?)` |
| `RepeatButton(label, onClick?)` | `(string, Action?)` |
| `ToggleButton(label, isChecked?, onToggled?)` | `(string, bool, Action<bool>?)` |
| `DropDownButton(label, flyout?)` | `(string, Element?)` |
| `SplitButton(label, onClick?, flyout?)` | `(string, Action?, Element?)` |
| `ToggleSplitButton(label, isChecked?, onChanged?, flyout?)` | `(string, bool, Action<bool>?, Element?)` |

## Input Controls

| Factory | Signature |
|---------|-----------|
| `TextBox(value, onChanged?, placeholder?)` | `(string, Action<string>?, string?)` |
| `PasswordBox(password, onPasswordChanged?, placeholderText?)` | `(string, Action<string>?, string?)` |
| `NumberBox(value, onValueChanged?, header?)` | `(double, Action<double>?, string?)` |
| `AutoSuggestBox(text, onTextChanged?, onQuerySubmitted?)` | `(string, Action<string>?, Action<string>?)` |
| `CheckBox(isChecked, onChanged?, label?)` | `(bool, Action<bool>?, string?)` |
| `RadioButton(label, isChecked?, onChecked?, groupName?)` | `(string, bool, Action<bool>?, string?)` |
| `RadioButtons(items, selectedIndex?, onSelectionChanged?)` | `(string[], int, Action<int>?)` |
| `ComboBox(items, selectedIndex?, onSelectionChanged?)` | `(string[], int, Action<int>?)` |
| `Slider(value, min?, max?, onChanged?)` | `(double, double, double, Action<double>?)` |
| `ToggleSwitch(isOn, onChanged?, onContent?, offContent?)` | `(bool, Action<bool>?, string?, string?)` |
| `RatingControl(value?, onValueChanged?)` | `(double, Action<double>?)` |
| `ColorPicker(color, onColorChanged?)` | `(Windows.UI.Color, Action<Windows.UI.Color>?)` |
| `RichEditBox(text?, onTextChanged?)` | `(string, Action<string>?)` |
| `ThreeStateCheckBox(checkedState?, onChanged?, label?)` | `(bool?, Action<bool?>?, string?)` |

## Date & Time

| Factory | Signature |
|---------|-----------|
| `CalendarDatePicker(date?, onDateChanged?)` | `(DateTimeOffset?, Action<DateTimeOffset?>?)` |
| `DatePicker(date, onDateChanged?)` | `(DateTimeOffset, Action<DateTimeOffset>?)` |
| `TimePicker(time, onTimeChanged?)` | `(TimeSpan, Action<TimeSpan>?)` |

## Progress & Status

| Factory | Signature |
|---------|-----------|
| `Progress(value)` | `double` (determinate) |
| `ProgressIndeterminate()` | spinning |
| `ProgressRing()` | indeterminate ring |
| `ProgressRing(value)` | determinate ring |
| `InfoBar(title?, message?)` | `(string?, string?)` |
| `InfoBadge()` / `InfoBadge(value)` | info badge |

## Layout & Containers

| Factory | Signature | Notes |
|---------|-----------|-------|
| `VStack(children...)` | `params Element?[]` | Vertical, spacing 8 |
| `VStack(spacing, children...)` | `(double, params Element?[])` | Custom spacing |
| `HStack(children...)` | `params Element?[]` | Horizontal, spacing 8 |
| `HStack(spacing, children...)` | `(double, params Element?[])` | Custom spacing |
| `ScrollView(child)` | `Element` | Scrollable container (modern `Microsoft.UI.Xaml.Controls.ScrollView` — default choice) |
| `ScrollViewer(child)` | `Element` | Classic `Microsoft.UI.Xaml.Controls.ScrollViewer` — reach for this when you need parallax animations, `ScrollViewer.SetXxx` attached props on templated parents, or the `IsIntermediate` view-changed flag |
| `Border(child)` | `Element` | Bordered container — raw |
| `Card(child)` | `Element → BorderElement` | Preset card: `Theme.CardBackground` + 1px `Theme.CardStroke` + 8px corner radius + 16px padding |
| `Expander(header, content, isExpanded?, onExpandedChanged?)` | See type | Collapsible |
| `SplitView(pane?, content?)` | `(Element?, Element?)` | Side pane |
| `Viewbox(child)` | `Element` | Scales child to fit |
| `Canvas(children...)` | `params CanvasChild[]` | Absolute positioning |
| `CanvasItem(element, left?, top?)` | `(Element, double, double)` | Positioned |
| `Flex(children...)` | `params Element?[]` | CSS Flexbox (row default) |
| `Flex(direction, children...)` | `(FlexDirection, params Element?[])` | Flex with direction |
| `FlexRow(children...)` | `params Element?[]` | Flex row shortcut |
| `FlexColumn(children...)` | `params Element?[]` | Flex column shortcut |
| `WrapGrid(children...)` | `params Element?[]` | Wrapping grid |
| `RelativePanel(children...)` | `params Element?[]` | Relative positioning |

### Grid

```csharp
Grid(
    columns: [GridSize.Star(), GridSize.Star(), GridSize.Px(200)],
    rows: [GridSize.Auto, GridSize.Star()],
    TextBlock("A").Grid(row: 0, column: 0),
    TextBlock("Wide").Grid(row: 1, column: 0, columnSpan: 3)
)
```

Track helpers: `GridSize.Star()` (1 star), `GridSize.Star(2)` (2 stars),
`GridSize.Auto` (size to content), `GridSize.Px(200)` (fixed 200px).

The legacy string-form overload (`Grid(["*", "*", "200"], ["Auto", "*"], …)`)
still compiles but is soft-deprecated (`CS0618`) — prefer the typed helpers
(spec 033 §1).

### Flex Layout (CSS Flexbox)

FlexPanel uses Yoga (Meta's layout engine) under the hood. Property names
map directly to CSS.

| CSS | Container Property | Enum |
|---|---|---|
| `flex-direction` | `Direction` | `FlexDirection` { Row, RowReverse, Column, ColumnReverse } |
| `justify-content` | `JustifyContent` | `FlexJustify` { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround, SpaceEvenly } |
| `align-items` | `AlignItems` | `FlexAlign` { FlexStart, Center, FlexEnd, Stretch, Baseline } |
| `align-content` | `AlignContent` | `FlexAlign` (+ SpaceBetween, SpaceAround, SpaceEvenly) |
| `flex-wrap` | `Wrap` | `FlexWrap` { NoWrap, Wrap, WrapReverse } |
| `column-gap` | `ColumnGap` | `double` |
| `row-gap` | `RowGap` | `double` |

Child `.Flex()` parameters: `grow` (default 0), `shrink` (default 1),
`basis`, `alignSelf`, `position`, `left/top/right/bottom`.

```csharp
// Holy-grail layout
FlexColumn(
    Flex(TextBlock("Header")).Flex(shrink: 0),
    FlexRow(
        TextBlock("Nav").Flex(basis: 200, shrink: 0),
        TextBlock("Main").Flex(grow: 1),
        TextBlock("Aside").Flex(basis: 150, shrink: 0)
    ).Flex(grow: 1),
    Flex(TextBlock("Footer")).Flex(shrink: 0))
```

**When to use what:** `VStack`/`HStack` for simple stacks. `Grid` for 2D
row/column with defined tracks. `Flex` for grow/shrink, wrapping, or
space distribution.

**Gotcha — `.Flex(grow: 1)` is `flex-grow: 1`, NOT CSS shorthand `flex: 1`.**
Reactor's `.Flex(...)` maps each parameter to the matching CSS individual
property. Default basis is `auto` (content size). When a growing child has
a large intrinsic size (`ListView`, `ScrollView` with many items, long
text), its content size makes the flex container overflow, and Yoga
shrinks **every** sibling proportionally — heading, buttons, and inputs
all collapse along with the scrollable region. Two ways to fix:

```csharp
// 1. Pass basis: 0 alongside grow — matches CSS `flex: 1` shorthand.
//    The grower starts at 0; siblings keep their natural size.
Border(ListView(items, ...)).Flex(grow: 1, basis: 0)

// 2. Put shrink: 0 on every fixed-size sibling.
FlexColumn(
    Heading("Title").Flex(shrink: 0),
    TextBox(name, setName).Flex(shrink: 0),
    Border(ListView(items, ...)).Flex(grow: 1))
```

Prefer (1) for a single growing region — one modifier, no per-sibling
bookkeeping. Use (2) when several children grow and you need basis=auto
on the growers (e.g., proportional columns sized by content).

Required: `using Microsoft.UI.Reactor.Layout;`

## Navigation

| Factory | Signature |
|---------|-----------|
| `NavigationView(menuItems, content?)` | `(NavigationViewItemData[], Element?)` |
| `NavItem(content, icon?, tag?)` | `(string, string?, string?)` |
| `TitleBar(title)` | `(string)` |
| `TabView(tabs...)` | `params TabViewItemData[]` |
| `Tab(header, content)` | `(string, Element)` |
| `BreadcrumbBar(items, onItemClicked?)` | `(BreadcrumbBarItemData[], Action<BreadcrumbBarItemData>?)` |
| `Breadcrumb(label, tag?)` | `(string, object?)` |
| `Pivot(items...)` | `params PivotItemData[]` |
| `PivotItem(header, content)` | `(string, Element)` |

## Collections

| Factory | Signature |
|---------|-----------|
| `ListView(items...)` | `params Element[]` |
| `GridView(items...)` | `params Element[]` |
| `TreeView(nodes...)` | `params TreeViewNodeData[]` |
| `TreeNode(content, children...)` | `(string, params TreeViewNodeData[])` |
| `FlipView(items...)` | `params Element[]` |
| `ListBox(items, selectedIndex?, onSelectionChanged?)` | `(string[], int, Action<int>?)` |

### Templated (data-driven, virtualized)

| Factory | Signature |
|---------|-----------|
| `ListView<T>(items, keySelector, viewBuilder)` | `(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` |
| `GridView<T>(items, keySelector, viewBuilder)` | same shape |
| `FlipView<T>(items, keySelector, viewBuilder)` | same shape |
| `LazyVStack<T>` / `LazyHStack<T>` | virtualized via ItemsRepeater |
| `ItemsView<T>` | same shape |

## Dialogs & Overlays

| Factory | Signature |
|---------|-----------|
| `ContentDialog(title, content, primaryButtonText?)` | `(string, Element, string)` |
| `Flyout(target, flyoutContent)` | `(Element, Element)` |
| `TeachingTip(title, subtitle?)` | `(string, string?)` |
| `ContentFlyout(content, placement?)` | `(Element, FlyoutPlacementMode)` |
| `Popup(child, isOpen?, onClosed?)` | `(Element, bool, Action?)` |
| `RefreshContainer(content, onRefreshRequested?)` | `(Element, Action?)` |
| `CommandBarFlyout(target, primaryCommands?, secondaryCommands?)` | See type |

## Menus

| Factory | Signature |
|---------|-----------|
| `MenuBar(items...)` | `params MenuBarItemData[]` |
| `Menu(title, items...)` | `(string, params MenuFlyoutItemBase[])` |
| `MenuItem(text, onClick?, icon?)` | `(string, Action?, string?)` |
| `MenuSeparator()` | — |
| `MenuSubItem(text, items...)` | `(string, params MenuFlyoutItemBase[])` |
| `MenuFlyout(target, items...)` | `(Element, params MenuFlyoutItemBase[])` |
| `CommandBar(primaryCommands?, secondaryCommands?)` | `(AppBarButtonData[]?, AppBarButtonData[]?)` |
| `AppBarButton(label, onClick?, icon?)` | `(string, Action?, string?)` |
| `AppBarToggleButton(label, isChecked?, onToggled?, icon?)` | `(string, bool, Action<bool>?, string?)` |
| `AppBarSeparator()` | — |
| `ToggleMenuItem(text, isChecked?, onToggled?, icon?)` | `(string, bool, Action<bool>?, string?)` |
| `RadioMenuItem(text, groupName, isChecked?, onClick?, icon?)` | See type |
| `MenuItems(items...)` | `params MenuFlyoutItemBase[]` |

See `skills/commanding.md` for command-aware overloads (`Button(cmd)`,
`MenuItem(cmd)`, etc.).

## Media

| Factory | Signature |
|---------|-----------|
| `Image(source)` | `string` |
| `PersonPicture()` | — |
| `WebView2(source?)` | `Uri?` |
| `MediaPlayerElement(source?)` | `string?` |
| `AnimatedVisualPlayer()` | — |

## Additional Controls

`SelectorBar`, `SelectorBarItem`, `PipsPager`, `AnnotatedScrollBar`,
`CalendarView`, `SemanticZoom`, `SwipeControl`, `AnimatedIcon`,
`ParallaxView`, `MapControl`, `Frame` — see SKILL.md or source for
signatures.

## Shapes

`Rectangle()`, `Ellipse()` — both `.Fill(brush)`.

## Rich Text & Markdown

| Factory | Signature |
|---------|-----------|
| `RichTextBlock(paragraphs)` | `RichTextParagraph[]` |
| `Paragraph(inlines...)` | `params RichTextInline[]` |
| `Run(text)` | `string` |
| `Hyperlink(text, navigateUri)` | `(string, Uri)` |
| `Markdown(markdown)` | `string → Element` |
| `Markdown(markdown, options)` | `(string, MarkdownOptions) → Element` |

## Icons

| Factory | Signature |
|---------|-----------|
| `SymbolIcon(symbol)` | `string` |
| `FontIcon(glyph, fontFamily?, fontSize?)` | `(string, string?, double?)` |
| `BitmapIcon(source, showAsMonochrome?)` | `(Uri, bool)` |
| `PathIcon(data)` | `string` |
| `ImageIcon(source)` | `Uri` |

## Utilities

| Factory | Signature |
|---------|-----------|
| `Thick(uniform)` | `double → Thickness` |
| `Thick(horizontal, vertical)` | `(double, double) → Thickness` |
| `Thick(left, top, right, bottom)` | `(double, double, double, double) → Thickness` |
| `Accelerator(key, modifiers?)` | `(VirtualKey, VirtualKeyModifiers) → KeyboardAcceleratorData` |
| `AcrylicBrush(tintColor, ...)` | `→ AcrylicBrush` |

## Conditional Helpers

| Factory | Purpose | Signature |
|---------|---------|-----------|
| `When(condition, then)` | Render only if true | `(bool, Func<Element>) → Element` |
| `If(condition, then, otherwise?)` | If/else | `(bool, Func<Element>, Func<Element>?) → Element` |
| `Expr(render)` | Inline block-expression escape hatch — evaluates the lambda and returns its element (or `EmptyElement` for null). No node, no hook scope, no memoization (spec 033 §5). | `(Func<Element?>) → Element` |
| `ForEach(items, render)` | Map list → elements | `(IEnumerable<T>, Func<T, Element>) → Element` |
| `ForEach(items, render)` | With index | `(IEnumerable<T>, Func<T, int, Element>) → Element` |
| `Empty()` | Renders nothing | `→ Element` |

## Function Components

| Factory | Purpose | Signature |
|---------|---------|-----------|
| `Memo(render)` | Inline component, render once + own state changes | `(Func<RenderContext, Element>) → MemoElement` |
| `Memo(render, deps)` | Inline component, also re-renders when any dep changes | `(Func<RenderContext, Element>, params object?[]) → MemoElement` |
| `RenderEachTime(render)` | Inline component, re-renders on every parent render (no memoization). Use sparingly. | `(Func<RenderContext, Element>) → FuncElement` |

The legacy `Func(ctx => …)` factory is soft-deprecated (`CS0618`) — replace
with `Memo(ctx => …)` for the common case or `RenderEachTime(ctx => …)` when
you specifically want the always-re-render shape (spec 033 §4).

---

## Fluent Modifiers

Modifiers return `Element`, so type-specific sugar (`.Bold()`,
`.CornerRadius()`) must come **before** generic modifiers (`.Margin()`).

### Layout (any Element)

```csharp
.Margin(16) / .Margin(h, v) / .Margin(l, t, r, b)
.Padding(...)
.Width(300) / .Height(200) / .Size(w, h)
.MinWidth(...) / .MinHeight(...) / .MaxWidth(...) / .MaxHeight(...)
.HAlign(HorizontalAlignment.Center) / .VAlign(VerticalAlignment.Top)
.Center()               // both H and V
.IsVisible(bool)          // Collapsed when false
.Opacity(0.6)
.ToolTip("text") / .WithToolTip(element)
.WithFlyout(flyout) / .WithContextFlyout(flyout)
.ApplyStyle(styleName)
.AutomationName("name")
.OnMount(action)
.Translation(x, y, z)
```

### Transitions (any Element)

```csharp
.OpacityTransition(duration?)
.RotationTransition(duration?)
.ScaleTransition(transition?)
.TranslationTransition(transition?)
.BackgroundTransition(duration?)
.WithTransitions(...)
.ItemContainerTransitions(...)
```

### Type-Specific Sugar (call BEFORE generic modifiers)

```csharp
TextBlock("Hello").Bold() / .SemiBold() / .FontSize(24) / .FontStyle(style)
Button("Click").IsEnabled(false) / .IsEnabled(!condition)
Border(child).CornerRadius(8).Background("#f5f5f5").WithBorder("#ccc", 1)
VStack(...).Spacing(16)
ComboBox(items).Placeholder("...").IsEditable()
NumberBox(v).Range(0, 100).SpinButtons()
Slider(v, 0, 100).StepFrequency(5)
RatingControl(3).MaxRating(10).IsReadOnly()
InfoBar("T", "M").Severity(InfoBarSeverity.Warning).IsClosable(false)
NavigationView(items, c).PaneDisplayMode(...).PaneTitle("...")
TitleBar("App").Subtitle("Home")
Expander("Hdr", body).Direction(ExpandDirection.Up)
PersonPicture().DisplayName("John Doe").Initials("JD")
ListView(items).SelectionMode(ListViewSelectionMode.Multiple)
TabView(tabs).IsAddTabButtonVisible()
ProgressRing().IsActive(active)
RepeatButton("Go", fn).Delay(d).Interval(i)
Rectangle().Fill(brush) / Ellipse().Fill(brush)
Popup(c, open).IsLightDismissEnabled(true).Offset(h, v)
ScrollView(c).ZoomMode(...).HorizontalScrollMode(...).VerticalScrollMode(...)         // modern; enums under WinUI.Scrolling*
ScrollViewer(c).ZoomMode(...).HorizontalScrollMode(...).VerticalScrollMode(...)        // classic; enums under WinUI.ScrollMode / .ZoomMode
TextBox(v, setV).Header("...")
element.WithKey("stable-id")  // always last
```

### `.Set()` — escape hatch to native WinUI

```csharp
Button("Go", fn).Set(b => b.FlowDirection = FlowDirection.RightToLeft)
TextBlock("Hello").Set(tb => tb.TextWrapping = TextWrapping.Wrap)
```

The lambda parameter is the real WinUI control — full IntelliSense.

---

## Enums

Patch uses WinUI types directly — add the usings:

```csharp
using Microsoft.UI.Xaml;           // Orientation, Thickness, *Alignment
using Microsoft.UI.Xaml.Controls;  // InfoBarSeverity, ExpandDirection, ...
```

| Namespace | Enum |
|---|---|
| `Microsoft.UI.Xaml` | `Orientation`, `HorizontalAlignment`, `VerticalAlignment`, `Thickness` |
| `Microsoft.UI.Xaml.Controls` | `InfoBarSeverity`, `ExpandDirection`, `ListViewSelectionMode`, `NavigationViewPaneDisplayMode`, `SplitViewDisplayMode`, `NumberBoxSpinButtonPlacementMode`, `ScrollBarVisibility`, `ContentDialogButton`, `ContentDialogResult`, `TreeViewSelectionMode`, `CommandBarDefaultLabelPosition` |
| `Microsoft.UI.Xaml.Controls.Primitives` | `FlyoutPlacementMode` |
| `Microsoft.UI.Text` | `FontWeights.Bold`, `.SemiBold`, `.Normal` |
| `Reactor.Flex` | `FlexDirection`, `FlexJustify`, `FlexAlign`, `FlexWrap`, `FlexPositionType` |

## Colors

Named: `"red"`, `"green"`, `"blue"`, `"white"`, `"black"`, `"gray"` /
`"grey"`, `"lightgray"`, `"transparent"`.

Hex: `"#RRGGBB"` or `"#AARRGGBB"` (e.g., `"#f5f5f5"`, `"#80000000"`).

For themed surfaces, prefer `Theme.*` tokens — see `skills/design.md`.

## Icon names

For NavigationView items, menu items, AppBarButtons — the `icon` parameter
takes a string mapping to `Symbol` enum. Case-insensitive. Common:
`"Home"`, `"Setting"`, `"Add"`, `"Delete"`, `"Save"`, `"Edit"`,
`"Find"`, `"Mail"`, `"Refresh"`, `"Download"`, `"Upload"`, `"Favorite"`,
`"Camera"`, `"People"`, `"Phone"`, `"Pin"`, `"OpenFile"`, `"Placeholder"`.
