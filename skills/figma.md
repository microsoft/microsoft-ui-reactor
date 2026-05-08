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

**Prerequisites:**
- A Figma MCP server must be configured (e.g., `figma-developer-mcp`) for URL-based extraction. See [spec 033](../docs/specs/033-figma-to-reactor.md) for the full workflow architecture.

## Workflow

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
| `paddingTop/Right/Bottom/Left` | uniform `P` | Wrap in `Border(VStack(...)).Padding(P)` |
| `paddingTop/Right/Bottom/Left` | mixed | Wrap in `Border(VStack(...)).Padding(left, top, right, bottom)` |

**Important:** `VStack` and `HStack` do not support `.Padding()` — only `Border` and control-based elements do. Always wrap the stack in a `Border` when padding is needed. Use `.Margin()` when the spacing is between the element and its siblings rather than internal padding.

### Sizing

| Figma Sizing Mode | Reactor Output |
|---|---|
| Fixed width/height | `.MinWidth(N)` / `.MinHeight(N)` (prefer over fixed `.Width(N)` / `.Height(N)` for text scaling) |
| Hug contents | No explicit size (natural sizing) |
| Fill container | `.HAlign(HorizontalAlignment.Stretch)` |
| Min/max constraints | `.MinWidth(N)` / `.MaxWidth(N)` |

Prefer `MinWidth`/`MinHeight` over fixed `Width`/`Height` on controls and text containers — fixed sizes clip content at larger text scales.

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

### Spacing Grid Rule

Round all spacing values to the **4px grid**: 4, 8, 12, 16, 20, 24, 32, 40, 48.

If a Figma value is not on the grid (e.g., 15px), round to nearest grid value (16px) and add a comment:

```csharp
// Note: Figma spacing was 15px, rounded to 16px (4px grid)
VStack(16, children)
```

## Control Mapping

### Tier 1 Controls (v1)

| Figma Component | Reactor Code |
|---|---|
| **Button** (Standard) | `Button(text, onClick)` |
| **Button** (Accent) | `Button(text, onClick).Resources(r => r.Set("ButtonBackground", Theme.Accent).Set("ButtonBackgroundPointerOver", Theme.AccentSecondary).Set("ButtonBackgroundPressed", Theme.AccentTertiary).Set("ButtonForeground", Theme.Ref("TextOnAccentFillColorPrimaryBrush")))` |
| **Button** (Subtle) | `Button(text, onClick).Resources(r => r.Set("ButtonBackground", Theme.SubtleFill).Set("ButtonBackgroundPointerOver", Theme.Ref("SubtleFillColorSecondaryBrush")).Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush")).Set("ButtonBorderBrush", Theme.SubtleFill))` |
| **TextBox** | `TextField(value, placeholder: "Placeholder", onChanged: v => { })` |
| **CheckBox** | `CheckBox(text, isChecked, (s, e) => { })` |
| **ToggleSwitch** | `ToggleSwitch(isOn, header: "Label", onToggle: (s, e) => { })` |
| **RadioButton** | `RadioButton(text, isChecked, onChecked: (s, e) => { })` |
| **ComboBox** | `ComboBox(items, selectedIndex, onSelectionChanged: (s, e) => { })` |
| **Slider** | `Slider(value, min: 0, max: 100, onValueChanged: (s, e) => { })` |
| **ProgressBar** | `ProgressBar(value)` or `ProgressBar()` for indeterminate |
| **ProgressRing** | `ProgressRing()` |
| **InfoBar** | `InfoBar(title: "Title", message: "Message", severity: InfoBarSeverity.Informational)` |
| **Expander** | `Expander(header: TextBlock("Header"), content: expandedContent)` |

### Surface Elements

| Figma Pattern | Reactor Code |
|---|---|
| **Card** (frame with card fill + border + 4px radius) | `Border(child).Background(Theme.CardBackground).WithBorder(Theme.CardStroke, 1).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)` |
| **Elevated card** (card with shadow) | Same as card + `.Translation(0, 0, 32).Set(b => b.Shadow = new ThemeShadow())` |
| **Divider** | `Border().Background(Theme.DividerStroke).Height(1)` |
| **Dialog shell** | `Border(content).Background(Theme.LayerFill).WithBorder(Theme.SurfaceStroke, 1).CornerRadius(ThemeResource.CornerRadius("OverlayCornerRadius").TopLeft).Padding(24)` |

### Unsupported Components

Any component not in Tier 1 → emit a placeholder:

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
   - Prefer `MinWidth`/`MinHeight` over fixed `Width`/`Height` on controls and text containers (§5).
   - Use `.HAlign()` / `.VAlign()` for alignment — not `.HorizontalAlignment()` / `.VerticalAlignment()` (§5).
   - Set `HorizontalContentAlignment = Stretch` on vertical `ScrollView` (§5).
   - Add `.AutomationName()` on icon-only interactive controls; use `.AccessibilityHidden()` on decorative icons (§7).
   - Add `.HeadingLevel()` on headings for screen reader navigation (§7).
   - Add `.Landmark()` on major page regions (§7).
   - Use `SymbolThemeFontFamily` for icon font glyphs (§4).
   - For circular elements, derive radius from size (`size / 2`) instead of hardcoded values (§5).
4. **Use Theme tokens first** — follow the token resolution ladder strictly. No hex on themed surfaces.
5. **Round to 4px grid** — all spacing values must be multiples of 4.
6. **Use theme resources for corner radii** — `ControlCornerRadius` (4px) and `OverlayCornerRadius` (8px). Do not hardcode number values.
7. **Emit TODO for unknowns** — never silently skip or guess. Every unmapped element gets a placeholder with the Figma node ID.
8. **One Component per top-level frame** — each major Figma frame becomes a Reactor Component class.
9. **Declare fidelity level** — every generated file states Level 1/2/3 in the header comment.
10. **Don't generate interaction logic** — emit empty event handlers `() => { }`. The developer fills in behavior.
11. **Don't set WinUI defaults** — don't emit `.Foreground(Theme.PrimaryText)` on body text, don't set default font size/family, don't set `HorizontalAlignment.Left` on left-aligned items.
12. **Keep generated code readable** — indent properly, use meaningful variable names, add whitespace between logical sections, add section comments from Figma layer names.
