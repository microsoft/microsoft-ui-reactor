---
name: reactor-figma
description: >
  Figma-to-Reactor translation rules. Maps Figma design elements from
  the Windows UI Kit to Reactor C# code — layout containers, WinUI
  controls, Theme tokens, typography, and corner radii. Load this when
  translating a Figma design into a Reactor app. Requires the Figma MCP
  server (figma-developer-mcp) for reading Figma files.
---

# Figma → Reactor Translation

Translate Figma designs built with the [Windows UI Kit (Community)](https://www.figma.com/design/t7yLwpMUOWJSYt5ahz3ROC/Windows-UI-kit--Community-) into Reactor C# code. This skill provides the mapping tables and rules the agent applies during code generation.

**Goal: Pixel-accurate reproduction.** Every dimension, gap, margin, padding, and position value from Figma must be used exactly as specified — no rounding, no substitution. The generated app should match the Figma design pixel-for-pixel. Where `design.md` recommends flexible sizing (MinWidth) or 4px grid rounding for hand-written code, those rules are overridden here — Figma translation uses exact values.

**Prerequisites:**
- A Figma MCP server must be configured (e.g., `figma-developer-mcp`) for URL-based extraction. See [spec 033](../docs/specs/033-figma-to-reactor.md) for the full workflow architecture.

## Workflow

### One-Shot Translation (paste a URL)

1. Developer pastes a Figma frame URL
2. Agent extracts `file_key` and `node_id` from the URL
3. Agent calls Figma MCP to get the scoped design context for that node
4. Agent filters out nodes with `visible: false` and their subtrees
5. Agent reads THIS skill file to map Figma → Reactor
6. Agent applies `design.md` best practices (typography, layout, accessibility)
7. Agent generates Component `.cs` files
8. Agent scaffolds project if needed (`mur --create`)
9. Agent launches app via `dotnet run -- --devtools run`
10. Agent verifies via `mur devtools tree` and `mur devtools screenshot`
11. Agent iterates until structure matches

### Watch-Based Sync (continuous)

For iterating alongside a designer making live edits in Figma:

1. Ensure `FIGMA_API_KEY` is set (same token as the Figma MCP server)
2. Agent starts the watch as a background shell process:
   ```bash
   mur figma watch "<figma-url>" --interval 10
   ```
3. The watch command polls the Figma REST API `lastModified` timestamp
4. When a change is detected, it emits a JSON event to stdout:
   ```json
   {"event":"changed","fileKey":"abc123","nodeId":"29792:125378","fileName":"My Design","lastModified":"2026-05-08T10:30:00Z"}
   ```
5. Agent reads the event and re-fetches design data via the Figma MCP `get_figma_data` tool using the `figmaUrl` from the event
6. Agent diffs the new tree against the previously generated code
7. Agent applies targeted edits (text, spacing, sizing) or regenerates as needed
8. `dotnet watch` picks up the code changes and hot-reloads the app

**No bridge server, no open ports, no Figma plugin required.** Authentication
uses the same `FIGMA_API_KEY` token as the Figma MCP server.

## URL Parsing

Extract `file_key` and `node_id` from the Figma URL:

```
https://www.figma.com/design/<file_key>/<name>?node-id=<node_id>&...
```

- `file_key` = the alphanumeric ID after `/design/`
- `node_id` = the `node-id` query parameter (format: `NNNNN-NNNNN`)

Pass the full URL to the Figma MCP tool — the server handles node scoping.

## Layout Mapping

### Auto-Layout Frames → VStack / HStack

| Figma Property | Value | Reactor Output |
|---|---|---|
| `layoutMode` | `VERTICAL` | `VStack(gap, children)` |
| `layoutMode` | `HORIZONTAL` | `HStack(gap, children)` |
| `itemSpacing` | `N` | Gap parameter: `VStack(N, ...)` or `HStack(N, ...)` |
| `paddingTop/Right/Bottom/Left` | all equal `P` | `.Padding(P)` on the wrapping `Border` |
| `paddingTop/Right/Bottom/Left` | symmetric (h≠v) | `.Padding(vertical, horizontal)` on the wrapping `Border` |
| `paddingTop/Right/Bottom/Left` | mixed | `.Padding(left: L, top: T, right: R, bottom: B)` on the wrapping `Border` |

**Important:** `VStack` and `HStack` do not support `.Padding()` — only `Border` and control-based elements do. Always wrap the stack in a `Border` when padding is needed. Use `.Margin()` when the spacing is between the element and its siblings rather than internal padding.

**Margin overloads:**
- `.Margin(uniform)` — all four sides equal
- `.Margin(vertical, horizontal)` — top/bottom share one value, left/right share another
- `.Margin(left: L, top: T, right: R, bottom: B)` — use named args for per-side values

### Sizing

| Figma Sizing Mode | Reactor Output |
|---|---|
| Fixed width | `.Width(N)` — use the **exact** pixel value from Figma |
| Fixed height | `.Height(N)` — use the **exact** pixel value from Figma |
| Fixed width + height | `.Size(W, H)` — shorthand for both |
| Hug contents | No explicit size (natural sizing) |
| Fill container | `.HAlign(HorizontalAlignment.Stretch)` |
| Fill container (vertical) | `.VAlign(VerticalAlignment.Stretch)` |
| Min/max constraints | `.MinWidth(N)` / `.MaxWidth(N)` / `.MinHeight(N)` / `.MaxHeight(N)` |

**Pixel-accuracy rule:** When Figma specifies a fixed width or height, always use the **exact** value via `.Width(N)` / `.Height(N)` / `.Size(W, H)`. Do NOT substitute `MinWidth`/`MinHeight` for fixed dimensions — that produces different layout behavior. Only use `MinWidth`/`MinHeight` when the Figma node explicitly has min/max constraints.

### Alignment

| Figma Alignment | Reactor Output |
|---|---|
| `MIN` (top/left) | Default — omit |
| `CENTER` | `.Center()` (both), `.VAlign(VerticalAlignment.Center)`, or `.HAlign(HorizontalAlignment.Center)` |
| `MAX` (bottom/right) | `.VAlign(VerticalAlignment.Bottom)` or `.HAlign(HorizontalAlignment.Right)` |
| `STRETCH` | `.HAlign(HorizontalAlignment.Stretch)` |

### Frames Without Auto-Layout

| Pattern | Reactor Output |
|---|---|
| Single child, has background/border | `Border(child).Background(fill).WithBorder(stroke)` |
| Multiple children, no layout | `Grid(children)` — flag as needing manual layout review |
| Absolute-positioned children | `Canvas(children)` with explicit `.Left()` / `.Top()` — flag for review |

### Scroll Regions

| Figma Property | Reactor Output |
|---|---|
| `overflowDirection: VERTICAL_SCROLLING` | `ScrollView(VStack(children)).Set(sv => sv.HorizontalContentAlignment = HorizontalAlignment.Stretch)` |
| `overflowDirection: HORIZONTAL_SCROLLING` | `ScrollView(HStack(children)).HorizontalScrollMode(ScrollMode.Auto).VerticalScrollMode(ScrollMode.Disabled)` |

Always set `HorizontalContentAlignment = Stretch` on vertical scroll regions to prevent content from collapsing. Place headers and footers outside the ScrollView so they remain fixed.

### Spacing Rule

Use the **exact** pixel values from Figma for all spacing, gaps, margins, and padding. Do NOT round to a grid. Pixel-accurate reproduction is the goal.

```csharp
// Figma says itemSpacing = 13 → use 13, not 12 or 16
VStack(13, children)

// Figma says padding top=18, left=24 → use exact values
Border(content).Padding(left: 24, top: 18)
```

## Control Mapping

### Windows UI Kit Component → Reactor Factory

Map Figma component instances from the Windows UI Kit to Reactor factory calls.
These are the **exact** factory signatures from `Dsl.cs` — use them precisely.

#### Basic Input

| Figma Component | Reactor Factory Call |
|---|---|
| **Button** (Standard) | `Button("Label", () => { })` |
| **Button** (Accent) | `Button("Label", () => { }).Set(b => b.Style = (Style)Application.Current.Resources["AccentButtonStyle"])` |
| **Button** (Subtle) | `Button("Label", () => { }).Resources(r => r.Set("ButtonBackground", Theme.SubtleFill).Set("ButtonBackgroundPointerOver", Theme.Ref("SubtleFillColorSecondaryBrush")).Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush")).Set("ButtonBorderBrush", Theme.SubtleFill))` |
| **Button** (with icon) | `Button(HStack(4, TextBlock("\uE710").Set(tb => tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]), TextBlock("Label")), () => { })` |
| **HyperlinkButton** | `HyperlinkButton("Link text", navigateUri: new Uri("https://..."))` |
| **ToggleButton** | `ToggleButton("Label", isChecked, v => setChecked(v))` |
| **RepeatButton** | `RepeatButton("Label", () => { })` |
| **DropDownButton** | `DropDownButton("Label", flyout: MenuItems(MenuItem("Option 1"), MenuItem("Option 2")))` |
| **SplitButton** | `SplitButton("Label", () => { }, flyout: MenuItems(MenuItem("Option")))` |
| **TextBox** | `TextField(value, onChanged: v => setValue(v), placeholder: "Placeholder", header: "Header")` |
| **PasswordBox** | `PasswordBox(password, onPasswordChanged: v => setPassword(v), placeholderText: "Password")` |
| **NumberBox** | `NumberBox(value, onValueChanged: v => setValue(v), header: "Amount")` |
| **AutoSuggestBox** | `AutoSuggestBox(text, onTextChanged: v => setText(v))` |
| **CheckBox** | `CheckBox(isChecked, onChanged: v => setChecked(v), label: "Label")` |
| **RadioButton** | `RadioButton("Option", isChecked, onChecked: v => { }, groupName: "group1")` |
| **RadioButtons** (group) | `RadioButtons(new[] { "Option 1", "Option 2" }, selectedIndex, i => setIndex(i))` |
| **ComboBox** | `ComboBox(new[] { "Item 1", "Item 2" }, selectedIndex, i => setIndex(i))` |
| **Slider** | `Slider(value, min: 0, max: 100, onChanged: v => setValue(v))` |
| **ToggleSwitch** | `ToggleSwitch(isOn, onChanged: v => setIsOn(v), header: "Label")` |
| **RatingControl** | `RatingControl(value, onValueChanged: v => setValue(v))` |
| **ColorPicker** | `ColorPicker(color, onColorChanged: c => setColor(c))` |

#### Date and Time

| Figma Component | Reactor Factory Call |
|---|---|
| **DatePicker** | `DatePicker(date, onDateChanged: d => setDate(d))` |
| **TimePicker** | `TimePicker(time, onTimeChanged: t => setTime(t))` |
| **CalendarDatePicker** | `CalendarDatePicker(date, onDateChanged: d => setDate(d))` |

#### Status and Info

| Figma Component | Reactor Factory Call |
|---|---|
| **ProgressBar** (determinate) | `Progress(value)` |
| **ProgressBar** (indeterminate) | `ProgressIndeterminate()` |
| **ProgressRing** (indeterminate) | `ProgressRing()` |
| **ProgressRing** (determinate) | `ProgressRing(value)` |
| **InfoBar** | `InfoBar(title: "Title", message: "Message").Set(ib => ib.Severity = InfoBarSeverity.Informational)` |
| **InfoBadge** | `InfoBadge(42)` or `InfoBadge()` for dot |
| **Tooltip** | `.ToolTip("Tooltip text")` modifier on any element |

#### Navigation

| Figma Component | Reactor Factory Call |
|---|---|
| **NavigationView** | `NavigationView(new[] { NavItem("Home", "\uE80F", "home"), NavItem("Settings", "\uE713", "settings") }, content: pageContent)` |
| **NavigationViewItem** | `NavItem("Label", "\uE80F", "tag")` — icon is Segoe MDL2 glyph |
| **NavigationViewItemHeader** | `NavItemHeader("Section")` |
| **TitleBar** | `TitleBar("App Title")` |
| **TabView** | `TabView(Tab("Tab 1", content1), Tab("Tab 2", content2))` |
| **BreadcrumbBar** | `BreadcrumbBar(items, onItemClicked: item => { })` |
| **Pivot** | `Pivot(PivotItem("Tab 1", content1), PivotItem("Tab 2", content2))` |
| **SelectorBar** | `SelectorBar(new[] { SelectorBarItem("Tab 1"), SelectorBarItem("Tab 2") }, selectedIndex, i => setIndex(i))` |

#### Layout

| Figma Component / Pattern | Reactor Factory Call |
|---|---|
| **Auto-layout VERTICAL** | `VStack(gap, children)` |
| **Auto-layout HORIZONTAL** | `HStack(gap, children)` |
| **VStack with spacing** | `VStack(16, child1, child2)` |
| **HStack with spacing** | `HStack(8, child1, child2)` |
| **Frame with padding** | `Border(child).Padding(16)` or `.Padding(left, top, right, bottom)` |
| **Frame with fill** | `Border(child).Background(Theme.CardBackground)` |
| **Frame with border** | `Border(child).WithBorder(Theme.CardStroke, 1)` |
| **ScrollView** (vertical) | `ScrollView(VStack(children)).Set(sv => sv.HorizontalContentAlignment = HorizontalAlignment.Stretch)` |
| **Expander** | `Expander("Header", content, isExpanded: false)` |
| **SplitView** | `SplitView(pane: sideContent, content: mainContent)` |

#### Collections

| Figma Component | Reactor Factory Call |
|---|---|
| **ListView** | `ListView(item1, item2, item3)` |
| **GridView** | `GridView(item1, item2, item3)` |
| **TreeView** | `TreeView(nodes)` |
| **FlipView** | `FlipView(page1, page2)` |

#### Dialogs and Flyouts

| Figma Component | Reactor Factory Call |
|---|---|
| **ContentDialog** | `ContentDialog("Title", content, primaryButtonText: "OK")` |
| **Flyout** | `Flyout(targetElement, flyoutContent)` |
| **MenuFlyout** | `MenuFlyout(targetElement, MenuItem("Item 1"), MenuItem("Item 2"))` |
| **TeachingTip** | `TeachingTip("Title", subtitle: "Description")` |

#### Menus and Toolbars

| Figma Component | Reactor Factory Call |
|---|---|
| **CommandBar** | `CommandBar(primaryCommands: new[] { AppBarButton("Add", () => { }, "\uE710") })` |
| **MenuBar** | `MenuBar(Menu("File", MenuItem("New"), MenuItem("Open")), Menu("Edit", MenuItem("Cut")))` |

#### Media

| Figma Component | Reactor Factory Call |
|---|---|
| **Image** | `Image("ms-appx:///Assets/photo.png")` |
| **PersonPicture** | `PersonPicture().Set(pp => pp.DisplayName = "Name")` |

### Surface Elements

| Figma Pattern | Reactor Code |
|---|---|
| **Card** (frame with card fill + border + 4px radius) | `Border(child).Background(Theme.CardBackground).WithBorder(Theme.CardStroke, 1).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)` |
| **Elevated card** (card with shadow) | Same as card + `.Translation(0, 0, 32).Set(b => b.Shadow = new ThemeShadow())` |
| **Divider** | `Border().Background(Theme.DividerStroke).Height(1)` |
| **Dialog shell** | `Border(content).Background(Theme.LayerFill).WithBorder(Theme.SurfaceStroke, 1).CornerRadius(ThemeResource.CornerRadius("OverlayCornerRadius").TopLeft).Padding(24)` |

### Unsupported Components

Any component not in the tables above → emit a placeholder:

```csharp
// TODO [Figma node XXXXX:YYYYY]: Unsupported component "ComponentName"
Border(
    TextBlock("[ComponentName]").Foreground(Theme.SecondaryText).Caption()
).Background(Theme.ControlFill).Padding(12).CornerRadius(4)
```

## Token Resolution

### Resolution Order (strict — first match wins)

1. **Figma variable → known Theme token** (use the table below)
2. **Hex value → known WinUI resource** (match against known WinUI palette)
3. **Repeated custom color (3+ uses)** → `AppTheme.Register()` with Light/Dark/HC
4. **One-off color** → literal hex with TODO comment

### Figma Variables → Reactor Theme Tokens

| Figma Variable | Reactor Token |
|---|---|
| `Fill Color/Text/Primary` | `Theme.PrimaryText` |
| `Fill Color/Text/Secondary` | `Theme.SecondaryText` |
| `Fill Color/Text/Tertiary` | `Theme.TertiaryText` |
| `Fill Color/Text/Disabled` | `Theme.DisabledText` |
| `Fill Color/Accent Text/Primary` | `Theme.AccentText` |
| `Fill Color/Accent/Default` | `Theme.Accent` |
| `Fill Color/Accent/Secondary` | `Theme.AccentSecondary` |
| `Fill Color/Accent/Tertiary` | `Theme.AccentTertiary` |
| `Fill Color/Accent/Disabled` | `Theme.AccentDisabled` |
| `Fill Color/Control/Default` | `Theme.ControlFill` |
| `Fill Color/Control/Secondary` | `Theme.ControlFillSecondary` |
| `Fill Color/Control/Tertiary` | `Theme.ControlFillTertiary` |
| `Fill Color/Control/Disabled` | `Theme.ControlFillDisabled` |
| `Fill Color/Control/Input Active` | `Theme.ControlFillInputActive` |
| `Fill Color/Subtle/Transparent` | `Theme.SubtleFill` |
| `Fill Color/Card Background/Default` | `Theme.CardBackground` |
| `Fill Color/Layer/Default` | `Theme.LayerFill` |
| `Fill Color/Solid Background/Base` | `Theme.SolidBackground` |
| `Fill Color/Smoke/Default` | `Theme.SmokeFill` |
| `Stroke Color/Card/Default` | `Theme.CardStroke` |
| `Stroke Color/Surface/Default` | `Theme.SurfaceStroke` |
| `Stroke Color/Divider/Default` | `Theme.DividerStroke` |
| `Stroke Color/Control/Default` | `Theme.ControlStroke` |
| `Stroke Color/Control/Secondary` | `Theme.ControlStrokeSecondary` |
| `Fill Color/System/Attention` | `Theme.SystemAttention` |
| `Fill Color/System/Success` | `Theme.SystemSuccess` |
| `Fill Color/System/Caution` | `Theme.SystemCaution` |
| `Fill Color/System/Critical` | `Theme.SystemCritical` |
| `Fill Color/System/Neutral` | `Theme.SystemNeutral` |
| `Fill Color/System/Attention Background` | `Theme.SystemAttentionBackground` |
| `Fill Color/System/Success Background` | `Theme.SystemSuccessBackground` |
| `Fill Color/System/Caution Background` | `Theme.SystemCautionBackground` |
| `Fill Color/System/Critical Background` | `Theme.SystemCriticalBackground` |

### Promoting Custom Colors to AppTheme

When a color appears 3+ times and doesn't match any WinUI token:

```csharp
// In Theme/AppBrushes.cs
AppTheme.Register(theme => theme
    .Add("BrandPrimaryBrush",
        light: "#005A9E",        // from Figma light mode
        dark: "#4FC3F7",         // from Figma dark mode, or adjust for contrast
        highContrast: "SystemColorHighlightColorBrush"));
```

If only a light-mode value is available from Figma, the agent should:
- Derive a dark-mode value (lighter/desaturated variant for contrast)
- Use an appropriate HC system brush
- Flag for designer review

## Typography

Use WinUI semantic text styles via `.ApplyStyle()` or Reactor text factories. Do not set `FontSize` and `FontWeight` directly for standard UI text — see `design.md` §4.

### Reactor Text Factories (Preferred)

| Figma Text Style | Reactor Code |
|---|---|
| Caption (12/16, Regular) | `Caption(text)` |
| Body (14/20, Regular) | `TextBlock(text)` — no modifier |
| Body Strong (14/20, SemiBold) | `TextBlock(text).SemiBold()` |
| Subtitle (20/28, SemiBold) | `SubHeading(text)` |
| Title (28/36, SemiBold) | `Heading(text)` |

### WinUI Style Tokens (for sizes without a factory)

| Figma Text Style | Reactor Code |
|---|---|
| Body Large (18/24, Regular) | `TextBlock(text).ApplyStyle("BodyLargeTextBlockStyle")` |
| Body Large Strong (18/24, SemiBold) | `TextBlock(text).ApplyStyle("BodyLargeTextBlockStyle").SemiBold()` |
| Title Large (40/52, SemiBold) | `TextBlock(text).ApplyStyle("TitleLargeTextBlockStyle")` |
| Display (68/92, SemiBold) | `TextBlock(text).ApplyStyle("DisplayTextBlockStyle")` |

**Rules:**
- Don't set font family — Segoe UI Variable is the WinUI default
- Don't set `Theme.PrimaryText` foreground on body text — it's the default
- Use `.Foreground(Theme.SecondaryText)` for captions and secondary labels
- Use `.TextWrapping(TextWrapping.WrapWholeWords)` on body text that should wrap
- Use `SymbolThemeFontFamily` for icon glyphs:
  ```csharp
  TextBlock("\uE710").Set(tb =>
      tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
  ```

## Corner Radius

| Figma Radius | Reactor Code |
|---|---|
| 4px | `ThemeResource.CornerRadius("ControlCornerRadius").TopLeft` |
| 8px | `ThemeResource.CornerRadius("OverlayCornerRadius").TopLeft` |
| Circular (50%) | `.CornerRadius(width / 2)` — for avatars/badges |
| 0px | No `.CornerRadius()` needed |
| Other values | Flag: `// TODO: non-standard corner radius Npx` |

Do NOT use hardcoded number values for 4px and 8px radii. Always use `ControlCornerRadius` and `OverlayCornerRadius` theme resources.

## Generated File Template

```csharp
// ═══════════════════════════════════════════════════════════
// FIGMA TRANSLATION SUMMARY
// Source: <figma_url>
// Fidelity: Level <1|2|3>
// Resolved: N/M visible elements
// TODOs: K items requiring manual review
//   - <description>
// Hidden in Figma (excluded): <list of hidden sections>
// ═══════════════════════════════════════════════════════════

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace MyApp.Components;

public class <ComponentName> : Component
{
    public override Element Render()
    {
        var controlCR = ThemeResource.CornerRadius("ControlCornerRadius");
        var overlayCR = ThemeResource.CornerRadius("OverlayCornerRadius");

        return <generated_tree>;
    }
}
```

## Preview Loop

### For AI agents (recommended)

```bash
# 1. Build and launch with devtools
dotnet run --project MyApp/MyApp.csproj -- --devtools run

# 2. Verify structure
mur devtools tree --pretty

# 3. Capture screenshot
mur devtools screenshot --out preview.png

# 4. After editing code, reload without restarting
mur devtools reload

# 5. Re-verify
mur devtools tree --pretty
mur devtools screenshot --out preview-v2.png
```

### For developers (hot-reload)

```bash
dotnet watch run --project MyApp/MyApp.csproj
```

Note: `dotnet watch` does not maintain MCP/devtools sessions across rebuilds. Use it for manual iteration only.

## Rules

1. **Always scope to a specific frame** — never call `get_file` on the entire Figma file.
2. **Skip hidden elements** — any Figma node with `visible: false` must be omitted from generated code entirely. Do not emit placeholders or comments for hidden nodes. Check visibility before processing each node and skip the entire subtree when a parent is hidden.
3. **Follow design.md best practices** — all generated code must comply with the `design.md` skill rules. Key requirements:
   - Use `.ApplyStyle()` or Reactor text factories (`Caption()`, `SubHeading()`, `Heading()`) for typography — not raw `FontSize`/`FontWeight` (§4).
   - `VStack`/`HStack` do not support `.Padding()` — wrap in `Border` (§5).
   - Use `.Width(N)` / `.Height(N)` / `.Size(W, H)` for fixed Figma dimensions. Only use `MinWidth`/`MinHeight` when Figma explicitly sets min/max constraints (§5).
   - Use `.HAlign()` / `.VAlign()` for alignment — not `.HorizontalAlignment()` / `.VerticalAlignment()` (§5).
   - Set `HorizontalContentAlignment = Stretch` on vertical `ScrollView` (§5).
   - Add `.AutomationName()` on icon-only interactive controls; use `.AccessibilityHidden()` on decorative icons (§7).
   - Add `.HeadingLevel()` on headings for screen reader navigation (§7).
   - Add `.Landmark()` on major page regions (§7).
   - Use `SymbolThemeFontFamily` for icon font glyphs (§4).
   - For circular elements, derive radius from size (`size / 2`) instead of hardcoded values (§5).
4. **Use Theme tokens first** — follow the token resolution ladder strictly. No hex on themed surfaces.
5. **Use exact pixel values** — do NOT round spacing, margins, padding, gaps, widths, or heights. Use the exact values from Figma for pixel-accurate output.
6. **Use theme resources for corner radii** — `ControlCornerRadius` (4px) and `OverlayCornerRadius` (8px). Do not hardcode number values.
7. **Emit TODO for unknowns** — never silently skip or guess. Every unmapped element gets a placeholder with the Figma node ID.
8. **One Component per top-level frame** — each major Figma frame becomes a Reactor Component class.
9. **Declare fidelity level** — every generated file states Level 1/2/3 in the header comment.
10. **Don't generate interaction logic** — emit empty event handlers `() => { }`. The developer fills in behavior.
11. **Don't set WinUI defaults** — don't emit `.Foreground(Theme.PrimaryText)` on body text, don't set default font size/family, don't set `HorizontalAlignment.Left` on left-aligned items.
12. **Keep generated code readable** — indent properly, use meaningful variable names, add whitespace between logical sections, add section comments from Figma layer names.
13. **Sanitize Figma text in string literals** — when embedding Figma text content as C# string literals, escape quotes (`"` → `\"`), backslashes (`\` → `\\`), and strip control characters (U+0000–U+001F). Never emit raw Figma text content outside of string literals. Figma collaborators can set arbitrary text; treat all text node content as untrusted input.
