---
name: reactor-design
description: >
  Windows 11 design system rules for Reactor UI — theming (Theme tokens,
  High Contrast), typography, 4px grid, acrylic surfaces, accessibility,
  animation, and a code-review checklist. Load this when authoring,
  reviewing, or fixing visual styling on a Reactor app.
---

# Windows 11 Design for Reactor

Author, review, and fix Reactor UI code following Windows 11 design system rules.

Reactor is a functional UI framework for WinUI 3 that builds UI entirely in C# — no XAML, no data binding, no ViewModels. UI is described with immutable Element records, composed via factory methods (`UI.Text()`, `UI.VStack()`, etc.), and updated through a React-style reconciler with hooks (`UseState`, `UseEffect`, etc.).

This skill translates the Windows 11 design language into Reactor's C# projection so that apps built with Reactor look, feel, and behave like first-class Windows 11 applications.

## Canonical Window Shell

**Every Reactor desktop app starts here.** This is the minimal shape that gives you a real Windows 11 caption, Mica material, and a content body sized to the window. Add features inside `body` — do not move the shell.

```csharp
ReactorApp.Run<App>("MyApp", width: 900, height: 600);

class App : Component
{
    public override Element Render()
    {
        var titleBar = TitleBar("MyApp").Flex(shrink: 0);

        var body = Border(
            FlexColumn(/* page content */) with { RowGap = 16 }
        ).Padding(24).Flex(grow: 1, basis: 0);

        return FlexColumn(titleBar, body)
            .Backdrop(BackdropKind.Mica);
    }
}
```

Why each line is load-bearing:
- `TitleBar(...)` — integrates with the Windows caption (drag region, system menu, min/max/close). Avoids the double-header trap of stacking a custom header row below the system chrome.
- `.Flex(shrink: 0)` on the title bar — title bar keeps its natural height; only `body` absorbs vertical resizing.
- `Border(...).Padding(24)` — `FlexColumn` doesn't support `.Padding()`, so wrap. 24px is "Section" page padding from the 4px grid; bump to 36px for hero pages.
- `.Flex(grow: 1, basis: 0)` on body — body fills the window below the title bar.
- `RowGap = 16` — sibling spacing on the 4px grid; the default is 0.
- `.Backdrop(BackdropKind.Mica)` on the root — Win11 signature material. The root must not paint an opaque background (no `Theme.SolidBackground`) or Mica won't show through.

Use `BackdropKind.MicaAlt` for secondary app windows. `BackdropKind.DesktopAcrylic` fits short-lived secondary windows (e.g., a popped-out picker window). `BackdropKind` only applies to a hosting `Window.SystemBackdrop` — flyouts and `ContentDialog`s aren't separate windows, so they take their styling from theme resources, not from `.Backdrop(...)`.

For TitleBar features beyond the bare title, use `with`:

```csharp
TitleBar("MyApp") with
{
    Subtitle = currentDoc,           // document name to the right of the app title
    Content = ModeSwitcher(...),     // inline content centered in the caption
    RightHeader = StatusBadge(...),  // right-aligned inline content
}
```

## Building a New App: Workflow

When authoring a new app, page, or feature, follow these steps **in order**. Pick controls before writing layout; write layout before applying theme tokens; apply theme tokens before adding animation. Interleaving is the source of most rework.

### Step 1 — Identify the app type and anchor control

The anchor control is the top-level structural pattern. Picking the right one up front is the single biggest determinant of whether the app feels like Windows 11 or like a generic web page.

| App type | Anchor pattern | Reactor factories |
|----------|--------|----------|
| Settings / config tool | Left-pane navigation with grouped sections | `NavigationView(navItems, content)` + `SubHeading`s per section |
| Document / session editor | Tabs over full-width content | `TabView(Tab(...), Tab(...))` |
| Hierarchical browser (Explorer-shaped) | Tree + list + breadcrumb | `TreeView` + `ListView` + `BreadcrumbBar` |
| Developer tool / dashboard | Left nav + card grid | `NavigationView` + `Border` cards |
| 2–3 mode utility | Mode switcher over compact body | `SelectorBar(...)` over body |
| Single-purpose utility | One screen, no navigation | Just the canonical shell with content in `body` |
| Wizard / multi-step | Linear steps with progress | `Pivot(...)` or `BreadcrumbBar` |

### Step 2 — Map requirements to factories

Pick each control by what it does, not by what looks similar. Defaults match the Win11 design system.

**Navigation:** 2–7 sections → `NavigationView` (Left). Document-like tabs → `TabView`. Breadcrumb trail → `BreadcrumbBar`. 2–3 mutually-exclusive modes → `SelectorBar`. Long flat list of pages → `NavigationView` Top.

**Data display:** Vertical list → `ListView`. Tiles / image grid → `GridView`. Hierarchy → `TreeView`. Tabular → `ListView` with `Grid` rows. Master–detail → `ListView` + detail panel. Carousel → `FlipView`.

**Input:** Text → `TextBox`. Number → `NumberBox`. Search → `AutoSuggestBox`. Date → `CalendarDatePicker`. Time → `TimePicker`. Boolean → `ToggleSwitch`. Pick 1 of 2–3 → `RadioButtons`. Pick 1 of 4+ → `ComboBox`. Continuous value → `Slider`. Password → `PasswordBox`.

**Feedback:** Blocking decision → `ContentDialog`. Contextual action → `Flyout` / `MenuFlyout`. Onboarding callout → `TeachingTip`. Inline status banner → `InfoBar`. Non-blocking screen-reader announcement → `UseAnnounce()` — the returned handle exposes a `Region` element that must be rendered somewhere in the component tree, or announcements no-op.

**Validation:** Form fields → `FormField(content, label: "…")` — wires label + required indicator + error display + automation name to the inner control.

If you reach for a `Border` + `Button` to fake a control that exists, stop and use the real control.

### Step 3 — Commit to the canonical window shell

Use the shell verbatim. Don't customize the shell until the feature is wired up — early customization is where good defaults get lost.

### Step 4 — Lay out the body

- **Content fills the window.** No centered cards floating on empty backgrounds. Page padding is 24px (sections) or 36px (hero); the canonical shell uses 24px.
- **4px grid** for every margin, padding, gap, and size — see §5.
- **Prefer `FlexColumn` / `FlexRow`** as the default linear layout. Use `VStack`/`HStack` only when porting StackPanel code or when StackPanel's shrink-wrap cross-axis behavior is specifically wanted.
- **Use `Grid` with `GridSize.Star()`** for any row containing text that needs `TextTrimming`. `HStack` measures children with unbounded main-axis width, so trimming never fires inside it. `FlexRow` does pass a finite main-axis constraint to flex children — so trimming works there *as long as* the text participates in flex sizing (default `shrink: 1`); if you've pinned the text with `.Flex(shrink: 0)` or wrapped it in a non-flex child, fall back to `Grid`.
- **Sidebar layouts:** fixed 300–360px sidebar + flexible main with `.Flex(grow: 1)`. No equal-width 50/50 splits.

Then layer in, in order: theme tokens (§1) → typography (§4) → accessibility (§7) → animation (§9). The numbered rules below are the reference for each.

### Step 5 — Review

Run through the [Code Review Checklist](#code-review-checklist) at the bottom of this file. If you're an agent reviewing your own work, this is the final pass before reporting done.

## Design Anti-Patterns

These are the specific mistakes that make a Reactor app look like a port from another framework. Each row maps a common failure mode to the right pattern.

| ❌ Don't | ✅ Do Instead | Rule |
|----------|--------------|------|
| Centered card floating on an empty background | Content fills window, page padding 24–36px on a `Border` wrapping the body | §5 Layout |
| Custom title row stacked above the system chrome | `TitleBar(...)` with `Subtitle` / `Content` / `RightHeader` for inline content | §5 Window Title Bar |
| No `Backdrop(...)` on the main window root | `.Backdrop(BackdropKind.Mica)` on the root element | §8 Window Backdrops |
| Root paints `Theme.SolidBackground` *and* sets a backdrop | Drop the background — Mica needs to show through | §8 Window Backdrops |
| `HStack` containing text with `TextTrimming` (or `FlexRow` where the text isn't a flex child) | `Grid` with `GridSize.Star()` column for the trimmed text | §5 Container Choice |
| `Height(N)` on a text-containing element | `MinHeight(N)` — fixed height clips at larger text scales | §5 Sizing |
| Hardcoded hex (`"#FFFFFF"`, `"#000000"`) on a themed surface | `Theme.*` token (`Theme.CardBackground`, `Theme.PrimaryText`, ...) | §1 Theming |
| Raw `FontSize(28).FontWeight(...)` on `TextBlock` | `Heading()` / `SubHeading()` / `Caption()` factories, or `.ApplyStyle("TitleTextBlockStyle")` | §4 Typography |
| Setting `Background` directly on `Button` (loses hover / pressed / disabled) | `.Resources(r => r.Set("ButtonBackground", ...).Set("ButtonBackgroundPointerOver", ...) ...)` | §2 Lightweight Styling |
| Equal-width 50/50 column split | Fixed sidebar 300–360px + flexible main with `.Flex(grow: 1)` | §5 Layout |
| Custom toggle-button row for "Light / Dark / Auto" mode | `SelectorBar(...)` or `RadioButtons(...)` | Step 2 |
| Clickable `Border` or `TextBlock` for primary actions | Real `Button` / `HyperlinkButton` — needed for AT, keyboard, focus | §7 Accessibility |
| Icon-only `Button` without `.AutomationName("...")` | Always set `.AutomationName()` on icon-only buttons — required by `REACTOR_A11Y_001` | §7 Accessibility |
| `TextBox` without a label (relying on `placeholder` alone) | `FormField(TextBox(...), label: "Name")` — wires label and automation name | Step 2 |
| Spacer `Border` element used for layout gaps | `VStack(spacing, ...)` / `HStack(spacing, ...)` / `FlexColumn(...) with { RowGap = n }` | §5 Spacing |
| Unkeyed children in a dynamic list | `.WithKey(item.Id)` on every list item | §10 Reconciliation |
| `ScrollView` wrapping a `ListView` | `ListView` already scrolls — wrap content, not list controls | §5 ScrollView |
| Missing `using Microsoft.UI.Reactor.Core` | Required for `BackdropKind`, `Theme`, hooks, `Component`, `Element` | — |

## Core Rules (Always Apply)

### 1. Theming Is the Primary Lens

Use `Theme.*` tokens for all colors and brushes. Never hardcode hex colors for themed surfaces.

```csharp
// Correct: theme tokens
TextBlock("Hello").Foreground(Theme.PrimaryText)
Border(child).Background(Theme.CardBackground)
Button("Action").Background(Theme.Accent)

// Wrong: hardcoded colors on themed surfaces
TextBlock("Hello").Foreground("#000000")
Border(child).Background("#FFFFFF")
```

Hardcoded colors are acceptable only for:
- One-off decorative elements that intentionally ignore theming (e.g., brand logos)
- Explicit hit-test targets (`Background("#00000000")` for transparent hit areas)

#### Theme Token Reference

**Text:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.PrimaryText` | `TextFillColorPrimaryBrush` | Primary body text, titles, and labels |
| `Theme.SecondaryText` | `TextFillColorSecondaryBrush` | Captions, subtitles, and supplementary info |
| `Theme.TertiaryText` | `TextFillColorTertiaryBrush` | Placeholder text and disabled secondary content |
| `Theme.DisabledText` | `TextFillColorDisabledBrush` | Text in disabled controls or inactive states |
| `Theme.AccentText` | `AccentTextFillColorPrimaryBrush` | Hyperlinks, accent-colored labels, and interactive text |

**Accent fill:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.Accent` | `AccentFillColorDefaultBrush` | Primary accent buttons and controls at rest |
| `Theme.AccentSecondary` | `AccentFillColorSecondaryBrush` | Accent button hover state |
| `Theme.AccentTertiary` | `AccentFillColorTertiaryBrush` | Accent button pressed state |
| `Theme.AccentDisabled` | `AccentFillColorDisabledBrush` | Disabled accent controls |

**Control fill:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.ControlFill` | `ControlFillColorDefaultBrush` | Default rest state for standard controls |
| `Theme.ControlFillSecondary` | `ControlFillColorSecondaryBrush` | Hover state of standard controls |
| `Theme.ControlFillTertiary` | `ControlFillColorTertiaryBrush` | Pressed state of standard controls |
| `Theme.ControlFillDisabled` | `ControlFillColorDisabledBrush` | Disabled state of standard controls |
| `Theme.ControlFillInputActive` | `ControlFillColorInputActiveBrush` | Active/focused text input field backgrounds |

**Surfaces & backgrounds:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.SolidBackground` | `SolidBackgroundFillColorBaseBrush` | App and page-level background (base layer) |
| `Theme.CardBackground` | `CardBackgroundFillColorDefaultBrush` | Card and elevated surface backgrounds |
| `Theme.LayerFill` | `LayerFillColorDefaultBrush` | Flyout, dialog, and pane backgrounds |
| `Theme.SubtleFill` | `SubtleFillColorSecondaryBrush` | Subtle control highlights and section backgrounds |
| `Theme.SmokeFill` | `SmokeFillColorDefaultBrush` | Semi-transparent overlay behind modal dialogs |

**Stroke & border:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.CardStroke` | `CardStrokeColorDefaultBrush` | Borders on cards and elevated containers |
| `Theme.SurfaceStroke` | `SurfaceStrokeColorDefaultBrush` | Borders on flyouts, dialogs, and pane surfaces |
| `Theme.DividerStroke` | `DividerStrokeColorDefaultBrush` | Horizontal or vertical dividers between content |
| `Theme.ControlStroke` | `ControlStrokeColorDefaultBrush` | Borders on interactive controls at rest |
| `Theme.ControlStrokeSecondary` | `ControlStrokeColorSecondaryBrush` | Bottom edge of controls for depth effect |

**System signal:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.SystemAttention` | `SystemFillColorAttentionBrush` | Informational indicators and badges |
| `Theme.SystemSuccess` | `SystemFillColorSuccessBrush` | Success states and confirmations |
| `Theme.SystemCaution` | `SystemFillColorCautionBrush` | Warning states and cautionary indicators |
| `Theme.SystemCritical` | `SystemFillColorCriticalBrush` | Error states and critical alerts |
| `Theme.SystemNeutral` | `SystemFillColorNeutralBrush` | Neutral status indicators |
| `Theme.SystemSolidNeutral` | `SystemFillColorSolidNeutralBrush` | Solid neutral fill for icons and non-text indicators |

**System signal backgrounds:**

| Token | WinUI Resource | Purpose |
|-------|---------------|---------|
| `Theme.SystemAttentionBackground` | `SystemFillColorAttentionBackgroundBrush` | Background for attention/info banners |
| `Theme.SystemSuccessBackground` | `SystemFillColorSuccessBackgroundBrush` | Background for success banners and info bars |
| `Theme.SystemCautionBackground` | `SystemFillColorCautionBackgroundBrush` | Background for warning banners and info bars |
| `Theme.SystemCriticalBackground` | `SystemFillColorCriticalBackgroundBrush` | Background for error banners and info bars |
| `Theme.SystemNeutralBackground` | `SystemFillColorNeutralBackgroundBrush` | Background for neutral status banners |
| `Theme.SystemSolidAttention` | `SystemFillColorSolidAttentionBackgroundBrush` | Solid background for high-contrast attention surfaces |

For any WinUI resource not exposed as a named token, use `Theme.Ref("ResourceKeyBrush")`.

#### Custom Theme Resource References

```csharp
// Reference any WinUI resource by key
Border(child).Background(Theme.Ref("AcrylicBackgroundFillColorDefaultBrush"))
Border(child).WithBorder(Theme.Ref("SurfaceStrokeColorFlyoutBrush"), 1)
```

#### Per-Subtree Theme Override

Force a subtree to a specific theme variant:

```csharp
// Sidebar always renders in dark theme
VStack(sidebarContent).RequestedTheme(ElementTheme.Dark)
```

See [theme-aware-resources.md](design-docs/theme-aware-resources.md) for full token list and pairing rules.

### 2. Lightweight Styling (Per-Control Resource Overrides)

Override control template resources using `.Resources()` to customize visual states without replacing the entire template:

```csharp
// Correct: override button resources for a subtle button
Button("Action", onClick).Resources(r => r
    .Set("ButtonBackground", Theme.SubtleFill)
    .Set("ButtonBackgroundPointerOver", Theme.Ref("SubtleFillColorSecondaryBrush"))
    .Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush"))
    .Set("ButtonBorderBrush", Theme.SubtleFill)
    .Set("ButtonBorderBrushPointerOver", Theme.SubtleFill)
    .Set("ButtonBorderBrushPressed", Theme.SubtleFill))

// Wrong: setting Background directly — loses hover/pressed/disabled states
Button("Action", onClick).Background(Theme.SubtleFill)
```

Resource keys target the `SolidColorBrush` resource (ending in `Brush`), not the `Color`.

```csharp
// Correct
.Set("ButtonBackground", Theme.Ref("ControlFillColorDefaultBrush"))

// Wrong — Color, not Brush
.Set("ButtonBackground", Theme.Ref("ControlFillColorDefault"))
```

### 3. High Contrast Rules (Strict)

High Contrast users rely on a fixed set of 8 system color brushes. Your UI must work with these constraints.

**Allowed HC system brushes:**

| Brush | Purpose |
|-------|---------|
| `SystemColorWindowTextColorBrush` | Text on window background |
| `SystemColorWindowColorBrush` | Window/content background |
| `SystemColorHighlightTextColorBrush` | Selected text foreground |
| `SystemColorHighlightColorBrush` | Selection/hover background |
| `SystemColorButtonTextColorBrush` | Button text |
| `SystemColorButtonFaceColorBrush` | Button background |
| `SystemColorGrayTextColorBrush` | Disabled/inactive text |
| `SystemColorHotlightColorBrush` | Hyperlinks |

**HC color pairings:**

| Background | Foreground | Use Case |
|------------|------------|----------|
| `SystemColorWindowColor` | `SystemColorWindowTextColor` | General content |
| `SystemColorHighlightColor` | `SystemColorHighlightTextColor` | Selected/hover states |
| `SystemColorButtonFaceColor` | `SystemColorButtonTextColor` | Buttons |
| `SystemColorWindowColor` | `SystemColorGrayTextColor` | Disabled content |
| `SystemColorWindowColor` | `SystemColorHotlightColor` | Hyperlinks |

**Rules:**
- No hardcoded colors in HC mode.
- No opacity on elements or brushes in HC — encode translucency in the alpha channel for Light/Dark only.
- No accent colors or regular WinUI brushes in HC.
- No gradient animations in HC — use a single system brush.
- Use 2px border thickness for flyouts, dialogs, and cards in HC.
- **No partial theme updates** — when changing Light/Dark visual resources via `.Resources()`, include matching HC-safe values in the same change. Don't leave HC untested.
- **Interactive containers in HC need a highlight border** — for clickable cards or list items, add a `SystemColorHighlightColor` border in HC to indicate interactivity.
- **Empty HC dictionary is valid** — when `.Resources()` overrides target only Light/Dark and WinUI defaults already satisfy accessibility, you don't need to add HC-specific overrides.
- Set `HighContrastAdjustment` at app level to prevent system overrides:
  ```csharp
  Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
  ```

Using `Theme.*` tokens correctly means HC usually "just works" because WinUI resolves them to appropriate system colors. Test to verify.

### 4. Typography Must Use Semantic Styles

Use the predefined text factories or WinUI style tokens. Never set `FontSize` and `FontWeight` directly for standard UI text.

**Reactor text factories:**

| Factory | Size | Weight | Use Case |
|---------|------|--------|----------|
| `Caption("text")` | 12px | Regular | Small labels, timestamps |
| `TextBlock("text")` | 14px | Regular | Default body text |
| `Body("text")` | 14px | Regular | WinUI `BodyTextBlockStyle` body text |
| `BodyStrong("text")` | 14px | Semibold | Emphasized inline labels |
| `BodyLarge("text")` | 18px | Regular | Prominent body text |
| `Subtitle("text")` | 20px | Semibold | WinUI `SubtitleTextBlockStyle` — section headings |
| `SubHeading("text")` | 20px | 600 | Section headers, card titles (Reactor preset) |
| `Title("text")` | 28px | Semibold | WinUI `TitleTextBlockStyle` — page titles |
| `Heading("text")` | 28px | 700 | Page titles (Reactor preset, slightly heavier) |

The `Title`/`Subtitle`/`Body`/`BodyStrong`/`BodyLarge` factories map 1:1 to
WinUI's named TextBlock styles (Spec 039 §17.6). Prefer them when matching
WinUI design specs; the `Heading`/`SubHeading` factories are the older
Reactor presets and remain valid.

**Font weight helpers:**

| Helper | Weight | Use Case |
|--------|--------|----------|
| `.SemiBold()` | 600 | Emphasized body text, inline labels, field headers |
| `.Bold()` | 700 | Reserved for `Heading()` page titles — avoid elsewhere |

```csharp
TextBlock("Important label").SemiBold()
TextBlock("Page Title").Bold()
```

**WinUI type ramp (via `.ApplyStyle()`):**

| Style | Size | Weight | Recommendation |
|-------|------|--------|----------------|
| `CaptionTextBlockStyle` | 12px | Regular | Secondary labels, timestamps, footnotes |
| `BodyTextBlockStyle` | 14px | Regular | Default body text, paragraphs, descriptions |
| `BodyStrongTextBlockStyle` | 14px | Semibold | Emphasized body text, inline labels |
| `BodyLargeTextBlockStyle` | 18px | Regular | Prominent body text |
| `SubtitleTextBlockStyle` | 20px | Semibold | Section headings, card group labels |
| `TitleTextBlockStyle` | 28px | Semibold | Page titles, dialog headings |
| `TitleLargeTextBlockStyle` | 40px | Semibold | Primary page titles on feature pages |
| `DisplayTextBlockStyle` | 68px | Semibold | Hero banners — one per page at most |

For sizes not covered by the factories, use `.ApplyStyle()` to apply WinUI text block styles. This sets size, weight, line height, and optical sizing in one call:

```csharp
// Preferred: .ApplyStyle() for WinUI styles
TextBlock("Title").ApplyStyle("TitleTextBlockStyle")
TextBlock("Subtitle").ApplyStyle("SubtitleTextBlockStyle")
TextBlock("Body Strong").ApplyStyle("BodyStrongTextBlockStyle")
TextBlock("Caption").ApplyStyle("CaptionTextBlockStyle")

// Also correct: use the Reactor factories for common sizes
Heading("Page Title")
SubHeading("Section")
Caption("Fine print")

// Escape hatch: .Set() for styles not exposed via .ApplyStyle()
TextBlock("Prominent text").Set(tb => tb.Style =
    (Style)Application.Current.Resources["BodyLargeTextBlockStyle"])

// Wrong: raw font properties for standard UI text
TextBlock("Title").FontSize(28).FontWeight(new FontWeight(700))
```

**Rules:**
- Use `SemiBold` (600), never `Bold` (700) for emphasis — except `Heading()` which intentionally uses 700 for page titles.
- Minimum font size: 12px. Anything smaller makes complex scripts unreadable.
- Use `{ThemeResource SymbolThemeFontFamily}` for icon fonts via `.Set()`:
  ```csharp
  TextBlock("\uE710").Set(tb =>
      tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
  ```
- When icons and text are paired, **top-align both** in wrapping scenarios to prevent visual drift at larger text scales.
- **TextWrapping:** `NoWrap` is the default — use `TextWrapping.Wrap` or `TextWrapping.WrapWholeWords` when text should flow to multiple lines. Choose `WrapWholeWords` for body text to avoid mid-word breaks.
- **Smart tooltips for trimmed text:** When text is trimmed with `TextTrimming`, add a tooltip that only appears when the text is actually trimmed:
  ```csharp
  TextBlock(longText)
      .TextTrimming(TextTrimming.CharacterEllipsis)
      .ToolTip(longText)
  ```

See [typography-and-colors.md](design-docs/typography-and-colors.md) for the full type ramp and color token list.

### 5. Layout and Scaling

#### 4px Grid

Use multiples of 4 for all margins, padding, and sizing values. The spacing scale is built on a 4px base unit:

| Value | Name | Usage |
|-------|------|-------|
| 0px | Flush | No gap between elements |
| 2px | Hairline | Tight spacing between related inline items |
| 4px | Compact | Icon-to-label gaps and small control internals |
| 8px | Standard | Default gap between sibling controls |
| 12px | Relaxed | Padding inside cards and grouped sections |
| 16px | Spacious | Between distinct content sections |
| 24px | Section | Major section boundaries and card padding |
| 36px | Page | Page-level margins around content |
| 48px | Hero | Large spacing for hero areas and visual breaks |

```csharp
// Correct: multiples of 4
VStack(8, children).Padding(16)
Border(child).Margin(12).Padding(8)
HStack(4, items)

// Wrong: odd values cause blurry rendering at fractional scales
VStack(5, children).Padding(15)
Border(child).Margin(3)
```

#### Margin vs Padding

**Margin** pushes an element away from its neighbors. It works on every Reactor element.

**Padding** adds space between a container's edge and its content. It only works on `Border` and `Control`-based elements (`Button`, `TextBox`, etc.). Layout panels like `VStack`, `HStack`, and `Grid` do not support `.Padding()` — wrap content in a `Border` if you need inner padding on a stack.

| Element | `.Margin()` | `.Padding()` |
|---------|-------------|-------------|
| `Border` | ✓ | ✓ |
| `Button` | ✓ | ✓ |
| `TextBox` | ✓ | ✓ |
| `TextBlock` | ✓ | — |
| `VStack` | ✓ | — |
| `HStack` | ✓ | — |
| `Grid` | ✓ | — |
| `Image` | ✓ | — |

```csharp
// Margin — works on ALL elements
TextBlock("Hello").Margin(8)
VStack(children).Margin(16)
Border(child).Margin(12)

// Padding — only on Border and Control (Button, TextBox, etc.)
Border(child).Padding(16)    // ✓ works
Button("Go").Padding(12)     // ✓ works

// VStack/HStack don't support Padding — wrap in Border instead:
Border(
    VStack(8, items)
).Padding(16)  // ✓ padding applied to the Border
```

Both `.Margin()` and `.Padding()` accept three overloads:

```csharp
// Uniform — same on all sides
element.Margin(16)

// Horizontal, Vertical
element.Margin(horizontal: 24, vertical: 8)

// Per-side: left, top, right, bottom
element.Margin(left: 4, top: 8, right: 16, bottom: 24)
```

#### Corner Radius

Use system values — `ControlCornerRadius` (4px) for controls and `OverlayCornerRadius` (8px) for overlays.

WinUI provides two corner radius theme resources. Use `ThemeResource.CornerRadius()` to resolve the system values at render time, ensuring your UI adapts if these values are customized:

```csharp
// Preferred: resolve system values at render time
var controlRadius = ThemeResource.CornerRadius("ControlCornerRadius");
var overlayRadius = ThemeResource.CornerRadius("OverlayCornerRadius");

// Apply to elements
Border(child).CornerRadius(controlRadius.TopLeft)   // controls, buttons, cards
Border(dialog).CornerRadius(overlayRadius.TopLeft)   // dialogs, flyouts, menus

// Also acceptable: hardcoded system values
Border(child).CornerRadius(4)   // ControlCornerRadius equivalent
Border(child).CornerRadius(8)   // OverlayCornerRadius equivalent

// Selective rounding (top corners only)
Border(child).CornerRadius(8, 8, 0, 0)

// Wrong: non-standard radii (even if from Figma)
Border(child).CornerRadius(3)
Border(child).CornerRadius(6)
```

**Mixed radii for nested surfaces:** When nesting controls inside overlay containers, the outer container uses `OverlayCornerRadius` while inner controls use `ControlCornerRadius`:

```csharp
var cr = ThemeResource.CornerRadius("ControlCornerRadius");
var or = ThemeResource.CornerRadius("OverlayCornerRadius");

// Dialog with control-radius inner elements
Border(
    VStack(12,
        TextBox("", placeholderText: "Username").CornerRadius(cr.TopLeft),
        Button("Sign In", onClick)
            .Background(Theme.Accent)
            .CornerRadius(cr.TopLeft)
    ).Margin(24)
)
.Background(Theme.LayerFill)
.WithBorder(Theme.SurfaceStroke)
.CornerRadius(or.TopLeft)  // outer overlay radius
```

#### Sizing

```csharp
// Correct: MinHeight for flexible sizing
Button("Action").MinHeight(40)
VStack(children).MinWidth(200)

// Wrong: fixed Height clips at larger text scales
Button("Action").Height(32)
TextBox(text, setText).Height(30)
```

- Prefer `MinHeight`/`MinWidth` over fixed `Height`/`Width`.
- Avoid fixed widths on buttons — let content drive width.
- Buttons achieve correct height through padding, not explicit `Height`.

#### Container Choice

Pick the right container. **Prefer `FlexRow` / `FlexColumn` as the default for
linear layout** — they follow CSS Flexbox semantics (grow, shrink, basis, gap,
wrap, `justify-content`, `align-items`), which matches the mental model web-
trained engineers and designers already have. `VStack` / `HStack` remain a good
choice when you specifically want StackPanel's shrink-wrap cross-axis behavior
or are porting from existing StackPanel code.

| Need | Use | Not |
|------|-----|-----|
| Single child with background/border | `Border(child)` | `Grid` or `FlexColumn` with one child |
| Linear vertical layout | `FlexColumn(children)` (preferred), or `VStack(children)` | Nested `Grid` |
| Linear horizontal layout | `FlexRow(children)` (preferred), or `HStack(children)` | Nested `Grid` |
| Grow/shrink, wrapping, or `justify-content` distribution | `Flex(children)` with `.Flex(grow/shrink/basis)` on children | Complex nested stacks |
| Positional row/column layout | `Grid(columns, rows, children)` | Canvas or absolute positioning |
| Text that needs trimming | `Grid` with `"*"` column | `HStack` / `FlexRow` (children get unbounded main-axis width) |

Remove wrapper containers (`Border`, `Grid`, `FlexColumn`, `VStack`) that exist only for nesting without contributing layout, styling, or semantic purpose.

**Text trimming caveat:** `HStack` (StackPanel) and `FlexRow` both give children unbounded width on the main axis, so `TextTrimming` never activates inside them. Use a `Grid` with a `GridSize.Star()` column. Note: `GridSize.Auto` also sizes to content and prevents trimming — always use `GridSize.Star()` for the column that contains trimmable text.

```csharp
// Correct: Grid constrains width so trimming works
Grid(
    columns: [GridSize.Auto, GridSize.Star()],
    rows: [GridSize.Auto],
    Image(source).Size(32, 32).Grid(column: 0),
    TextBlock(title).TextTrimming(TextTrimming.CharacterEllipsis).Grid(column: 1))

// Wrong: TextTrimming never fires inside HStack
HStack(8,
    Image(source).Size(32, 32),
    TextBlock(title).TextTrimming(TextTrimming.CharacterEllipsis))
```

#### ScrollView Configuration

- Use `Auto` scrollbar visibility — scrollbar appears only when content overflows.
- Set `HorizontalContentAlignment = Stretch` on the ScrollView to prevent content from collapsing.
- Only the content area should scroll — headers and action bars remain outside the ScrollView.

```csharp
// Correct: header stays fixed, content scrolls
VStack(
    Heading("Page Title"),
    ScrollView(
        VStack(8, contentItems)
    ).Set(sv => sv.HorizontalContentAlignment = HorizontalAlignment.Stretch))
```

#### Window Title Bar

Prefer `TitleBar(...)` as the top-of-window element for Reactor desktop apps
where a title makes sense (most main windows, document shells, settings
windows). It integrates with the Windows caption (drag region, system menu,
min/max/close) and themes with the rest of the app, and it accepts inline
`Content` and `RightHeader` for branding, document context, or small inline
controls — which avoids the common mistake of stacking a custom header row
below the system chrome and ending up with two visual title zones.

```csharp
// Preferred: real Windows title bar with app title + subtitle + inline content
var titleBar = (TitleBar("MyApp") with
{
    Subtitle = currentDoc,
    Content = ModeSwitcher(mode, setMode),
    RightHeader = TextBlock($"{rowsLoaded:N0} rows").FontSize(12).Opacity(0.6),
}).Flex(shrink: 0);

return FlexColumn(titleBar, MainContent());
```

Small dialogs, embedded pop-outs, and tool windows that already have a
distinct visual header don't need `TitleBar` — use it where you'd otherwise
reach for a custom header row across the full window width.

#### Spacing

Use `FlexColumn(...) with { RowGap = n }` / `FlexRow(...) with { ColumnGap = n }` (or `VStack(spacing, ...)` / `HStack(spacing, ...)`) or Grid `RowSpacing`/`ColumnSpacing` — not spacer elements.

```csharp
// Correct
VStack(8, TextBlock("A"), TextBlock("B"), TextBlock("C"))

// Wrong: spacer element for spacing
VStack(
    TextBlock("A"),
    Border(null).Height(8),  // Don't do this
    TextBlock("B"))
```

#### Shadows

`ThemeShadow` requires elevation to be visible. Add `Translation(0, 0, 32)` and ensure the parent has padding (12px) to prevent shadow clipping:

```csharp
Border(
    ScrollView(VStack(16, content))
).Background(Theme.Ref("AcrylicBackgroundFillColorDefaultBrush"))
 .WithBorder(Theme.Ref("SurfaceStrokeColorFlyoutBrush"), 1)
 .CornerRadius(8)
 .Translation(0, 0, 32)
 .Set(b =>
 {
     b.BackgroundSizing = BackgroundSizing.InnerBorderEdge;
     b.Shadow = new ThemeShadow();
 })
```

See [layout-and-scaling.md](design-docs/layout-and-scaling.md) for full layout rules.

### 6. Data Flow and State

Reactor uses hooks, not MVVM data binding. Follow these patterns:

#### State-Driven UI (Preferred)

```csharp
var (items, setItems) = UseState(new List<Item>());
var (filter, setFilter) = UseState("");

var filtered = UseMemo(() =>
    items.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList(),
    items, filter);

return VStack(
    TextBox(filter, setFilter, placeholderText: "Filter..."),
    VStack(filtered.Select(item =>
        TextBlock(item.Name).WithKey(item.Id)
    ).ToArray()));
```

#### UseObservable for External Models

When integrating with existing `INotifyPropertyChanged` objects:

```csharp
var model = UseObservable(externalModel);

return VStack(
    TextBlock(model.Title),
    Slider(model.Volume, 0, 100, v => model.Volume = v));
```

#### Hook Rules (Critical)

1. **Same order every render** — no hooks inside `if` blocks, no hooks in variable-length loops.
2. **Only call from `Render()`** — or from within a function component body.
3. **Use `UseCallback` for stable references** — handlers passed to children should be memoized.

```csharp
// Correct: hooks at top level, unconditional
var (count, setCount) = UseState(0);
var (name, setName) = UseState("");
var increment = UseCallback(() => setCount(count + 1), count);

// Wrong: conditional hook
if (showCounter)
{
    var (count, setCount) = UseState(0);  // BREAKS hook ordering
}
```

### 7. Accessibility

#### Tier 1 Modifiers (Every Control)

These modifiers are zero-cost — apply them by default:

```csharp
// Icon-only buttons MUST have AutomationName
Button(Content: Image(iconSource), onClick)
    .AutomationName("Close dialog")

// Headings for screen reader navigation
Heading("Settings").HeadingLevel(AutomationHeadingLevel.Level1)
SubHeading("General").HeadingLevel(AutomationHeadingLevel.Level2)

// Tab order and access keys
Button("Save", onSave).IsTabStop(true).TabIndex(0).AccessKey("S")
```

| Modifier | Purpose |
|----------|---------|
| `.AutomationName("text")` | Screen reader label — required on icon-only controls |
| `.HeadingLevel(Level1..Level9)` | Heading hierarchy for screen reader navigation |
| `.IsTabStop(true/false)` | Include/exclude from tab order |
| `.TabIndex(n)` | Explicit tab order |
| `.AccessKey("X")` | Alt+X keyboard shortcut |

#### Tier 2 Modifiers (Lazy-Allocated)

These allocate an `AutomationProperties` peer only when set:

```csharp
TextBox(name, setName)
    .HelpText("Enter your full legal name")
    .Required()

// Hide decorative elements from screen readers
Image(decorativeSource).AccessibilityHidden()

// Position-in-set for screen readers on list items
items.Select((item, i) =>
    TextBlock(item.Name)
        .PositionInSet(i + 1, items.Count)
        .WithKey(item.Id))
```

| Modifier | Purpose |
|----------|---------|
| `.HelpText("text")` | Extended description (read after name) |
| `.FullDescription("text")` | Detailed description for complex elements |
| `.AccessibilityHidden()` | Hide decorative elements from AT |
| `.AccessibilityView(Raw/Control/Content)` | Control AT tree visibility |
| `.Landmark(LandmarkType)` | Navigation landmark (see below) |
| `.Required()` | Marks a field as required for AT |
| `.LiveRegion(Polite/Assertive)` | Announce dynamic content changes |
| `.PositionInSet(pos, size)` | Position in a virtual list for AT |

#### Navigation Landmarks

Landmarks let screen reader users jump between page regions:

```csharp
NavigationView(navItems, content)
    .Landmark(AutomationLandmarkType.Navigation)

FlexColumn(mainContent)
    .Landmark(AutomationLandmarkType.Main)

VStack(searchControls)
    .Landmark(AutomationLandmarkType.Search)

VStack(formFields)
    .Landmark(AutomationLandmarkType.Form)
```

#### Heading Hierarchy

Maintain a logical heading structure for screen reader navigation:

```csharp
// Good: proper hierarchy
Heading("Settings").HeadingLevel(AutomationHeadingLevel.Level1)
SubHeading("General").HeadingLevel(AutomationHeadingLevel.Level2)
SubHeading("Advanced").HeadingLevel(AutomationHeadingLevel.Level2)

// Bad: skipping levels
Heading("Settings").HeadingLevel(AutomationHeadingLevel.Level1)
Caption("Detail").HeadingLevel(AutomationHeadingLevel.Level4) // skipped 2 and 3
```

#### Focus Trapping (Dialogs / Modals)

Use `UseFocusTrap` to keep focus inside a modal:

```csharp
var trap = UseFocusTrap(isActive: true);

return Border(
    VStack(12,
        Heading("Confirm"),
        TextBlock("Are you sure?"),
        HStack(8,
            Button("Cancel", onCancel),
            Button("Confirm", onConfirm))
    ).Padding(24)
).FocusTrap(trap);  // Tab cycles within this container
```

`UseFocusTrap(isActive)` returns a `FocusTrapHandle`. Apply it with
`.FocusTrap(handle)`. Focus is trapped while `isActive` is true.

#### UseAnnounce (Live Announcements)

For dynamic status updates that aren't tied to a visible element:

```csharp
var announce = UseAnnounce();

// The announce region must be in the visual tree
return VStack(12,
    announce.Region,  // invisible — just hosts the live region
    Button("Save", async () =>
    {
        await Save();
        announce.Announce("Document saved successfully");
    }),
    Button("Error demo", () =>
        announce.Announce("Upload failed — check your connection", assertive: true))
);
```

**Important:** `announce.Region` must be in the visual tree or announcements
are silently lost. Place it once near the root of your page.

#### SemanticPanel (Custom Roles)

For controls that don't map to standard WinUI automation peers:

```csharp
Border(child).Semantics(
    role: "Slider",
    value: $"{percent}%",
    rangeMin: 0,
    rangeMax: 100,
    rangeValue: percent);
```

#### AccessibilityScanner (Automated Testing)

Run WCAG checks programmatically:

```csharp
var results = AccessibilityScanner.Scan(rootElement);

// Built-in checks include:
// - Missing AutomationName on interactive controls
// - Missing HeadingLevel on heading text
// - Color contrast ratio < 4.5:1
// - Touch target size < 44x44
// - Missing landmark regions
// - Tab order gaps
// - Missing PositionInSet on list items
// - AccessKey conflicts

AccessibilityScanner.ExportJson(results, "a11y-report.json");
```

#### Roslyn Analyzers

Three compile-time analyzers catch accessibility issues:

| Analyzer | Checks |
|----------|--------|
| `REACTOR_A11Y_001` | Icon-only Button/ToggleButton missing `.AutomationName()` |
| `REACTOR_A11Y_002` | Image missing `.AutomationName()` or `.AccessibilityHidden()` |
| `REACTOR_A11Y_003` | Form field (TextBox/NumberBox/PasswordBox) missing label |

These run as warnings by default. Promote to errors in CI:

```xml
<WarningsAsErrors>REACTOR_A11Y_001;REACTOR_A11Y_002;REACTOR_A11Y_003</WarningsAsErrors>
```

#### Accessible Forms Pattern

Combine validation with accessibility:

```csharp
var validation = UseValidationContext();
var (email, setEmail) = UseState("");

return FormField("Email",
    TextBox(email, setEmail)
        .Validate(validation, "email", Validators.Required(), Validators.Email())
        .Required(true)
        .HelpText("We'll send a confirmation to this address"),
    required: true,
    showErrorWhen: ShowWhen.Touched)
.Landmark(AutomationLandmarkType.Form);
```

`FormField` automatically wires `AutomationName` from the label and
associates error messages with the input via `DescribedBy`.

#### Accessibility Rules

- Set `AutomationName` on every control without visible text.
- Maintain heading hierarchy — don't skip levels.
- Use landmarks on major page regions.
- Focus-trap all dialogs and modals.
- Place `announce.Region` in the tree before calling `.Announce()`.
- Test with Narrator + keyboard-only navigation.
- Test Light, Dark, and High Contrast themes (especially NightSky).
- Test at 100%, 150%, 200%, 250% display scaling.
- Test with maximum text scaling (Settings > Accessibility > Text size).
- Hit-test targets for light-dismiss must be visible: `Background("#00000000")`.
- Use `DividerStrokeColorDefaultBrush` for dividers — custom brushes with opacity break in HC.
- Enable Roslyn analyzers in CI to catch common issues at build time.

See [code-review-checklist.md](design-docs/code-review-checklist.md) for the full accessibility checklist.

### 8. Acrylic Surface Pairings

Acrylic backgrounds have specific border pairings. Using the wrong combination produces incorrect visuals.

| Surface Type | Background | Border |
|--------------|------------|--------|
| Flyouts, tooltips | `AcrylicBackgroundFillColorDefaultBrush` | `SurfaceStrokeColorFlyoutBrush` |
| UI surfaces | `AcrylicBackgroundFillColorBaseBrush` | `SurfaceStrokeColorDefaultBrush` |

```csharp
// Correct: flyout acrylic pairing
Border(content)
    .Background(Theme.Ref("AcrylicBackgroundFillColorDefaultBrush"))
    .WithBorder(Theme.Ref("SurfaceStrokeColorFlyoutBrush"), 1)
    .CornerRadius(8)
    .Translation(0, 0, 32)
    .Set(b =>
    {
        b.BackgroundSizing = BackgroundSizing.InnerBorderEdge;
        b.Shadow = new ThemeShadow();
    })
```

**`BackgroundSizing = InnerBorderEdge`** is required on any bordered acrylic surface. It prevents the background from bleeding through the border edge. Always set it on acrylic containers with a border.

- Overlays on acrylic use `LayerOnAcrylicFillColorDefaultBrush`.
- Keep one acrylic layer per visual surface to avoid stacked-material artifacts.

#### Window Backdrops (Mica / Acrylic)

For *window-level* material (Mica/Acrylic showing through the title bar and
chrome), use the `.Backdrop(BackdropKind)` modifier on the root element —
not a brush. The modifier walks up to the host's `Window` and assigns the
right `SystemBackdrop`:

```csharp
return Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star()],
    titleBar.Grid(row: 0),
    content.Grid(row: 1)
).Backdrop(BackdropKind.Mica);
```

Available kinds: `BackdropKind.None`, `Mica`, `MicaAlt`, `DesktopAcrylic`,
`AcrylicThin`. On `ReactorHostControl` (windowless host) the modifier
no-ops cleanly. For Mica to show through, the root element must not
paint an opaque background — drop `Theme.SolidBackground` from the root
when applying a backdrop. Spec 033 §6.

#### Flyout Surface Pattern

Flyout/popup surfaces should follow a standard elevation pattern:

```csharp
Border(
    ScrollView(VStack(8, flyoutContent))
)
.Background(Theme.Ref("FlyoutPresenterBackground"))
.WithBorder(Theme.Ref("FlyoutBorderThemeBrush"), 1)
.CornerRadius(8)
.Translation(0, 0, 32)
.Set(b =>
{
    b.BackgroundSizing = BackgroundSizing.InnerBorderEdge;
    b.Shadow = new ThemeShadow();
})
```

Use `FlyoutPresenterBackground` and `FlyoutBorderThemeBrush` for standard popup surfaces. Use the explicit acrylic resource pairings (above) only when building custom surfaces that don't use WinUI's flyout presenter resources.

### 9. Animation and Motion

#### Implicit Transitions (Recommended)

Animate property changes smoothly — the framework handles interpolation:

```csharp
// Opacity fade
Border(child)
    .OpacityTransition()
    .Opacity(isVisible ? 1.0 : 0.0)

// Background color crossfade
VStack(children)
    .BackgroundTransition()
    .Background(isActive ? Theme.Accent : Theme.ControlFill)

// Scale bounce
Border(child)
    .ScaleTransition()
    .Scale(isPressed ? 0.95f : 1.0f)
```

#### Layout Animations

Animate children being added, removed, or repositioned:

```csharp
VStack(items.Select(item =>
    TextBlock(item.Name).WithKey(item.Id)
).ToArray()).LayoutAnimation()
```

#### Element Enter/Exit Transitions

```csharp
// Fade + slide from bottom on mount
Border(child).WithTransitions(
    Transition.Fade,
    Transition.Slide(Edge.Bottom))
```

**Rules:**
- Use `ThemeShadow` instead of composition drop shadows.
- Avoid gradient animations in High Contrast.
- Use `BrushTransition` (via `BackgroundTransition()`) for smooth color changes.
- Always set `Translation(0, 0, 32)` when using `ThemeShadow`.

### 10. Reconciliation and Performance

#### Keys for Lists

Always set `.WithKey()` on items in dynamic lists. Without keys, the reconciler matches by position, causing unnecessary re-mounts on insert/reorder:

```csharp
// Correct: keyed children — stable identity
VStack(items.Select(item =>
    HStack(8,
        Image(item.Avatar).Size(32, 32),
        TextBlock(item.Name)
    ).WithKey(item.Id)
).ToArray())

// Wrong: unkeyed — insert at index 0 re-renders everything
VStack(items.Select(item =>
    HStack(8,
        Image(item.Avatar).Size(32, 32),
        TextBlock(item.Name)
    ).ToArray())
```

#### Memoize Expensive Computations

```csharp
var sorted = UseMemo(() =>
    items.OrderBy(x => x.Name).ToList(),
    items);

var handler = UseCallback(() => save(count), count);
```

#### Avoid Deep Nesting

Flatten visual tree depth where possible. Use `Border` instead of single-child `Grid`/`VStack`.

```csharp
// Correct: flat
Border(TextBlock("Hello")).Background(Theme.CardBackground).Padding(16)

// Even better: the Card(child) factory bakes in CardBackground +
// CardStroke 1px hairline + 8px corner radius + 16px padding.
Card(TextBlock("Hello"))

// Wrong: unnecessary nesting
VStack(
    Grid([GridSize.Star()], [GridSize.Star()],
        TextBlock("Hello")
    ).Background(Theme.CardBackground)
).Padding(16)
```

#### Use `.Set()` Sparingly

`.Set()` is an escape hatch to raw WinUI. It's valid but bypasses the virtual element model — use it for properties Reactor doesn't expose, not as a general pattern.

```csharp
// Good: property not exposed by Reactor
TextBlock("Clock").Set(tb => tb.Typography.NumeralAlignment = FontNumeralAlignment.Tabular)

// Bad: property that Reactor exposes as a modifier
TextBlock("Hello").Set(tb => tb.Margin = new Thickness(16))  // Use .Margin(16) instead
```

### 11. Formatting Conventions

- Use `using static Microsoft.UI.Reactor.Factories;` to access the DSL without prefix.
- One element per line when nesting gets deep.
- Group modifiers logically: layout first, then appearance, then behavior.
- Use trailing `.WithKey()` as the last modifier.

```csharp
// Good: readable modifier order
Border(
    VStack(16,
        Heading("Title"),
        TextBlock("Description").Foreground(Theme.SecondaryText),
        HStack(8,
            Button("Cancel", onCancel),
            Button("Save", onSave).Resources(r => r
                .Set("ButtonBackground", Theme.Accent)
                .Set("ButtonForeground", Theme.Ref("TextOnAccentFillColorPrimaryBrush")))
        )
    )
)
.Padding(24)
.CornerRadius(8)
.Background(Theme.CardBackground)
.WithBorder(Theme.CardStroke, 1)
.AutomationName("Settings card")
```

### 12. Text Scaling and Localization

Every UI change must survive text scaling and long strings:

- Use `MinHeight` instead of `Height` on containers with text.
- Avoid fixed widths on buttons and text containers.
- Use `VAlign(VerticalAlignment.Center)` instead of margin-based centering.
- Use tabular numerals for changing numbers (clock, battery, progress):
  ```csharp
  TextBlock($"{percent}%").Set(tb =>
      tb.Typography.NumeralAlignment = FontNumeralAlignment.Tabular)
  ```

### 13. Avoid Setting WinUI Defaults

Do not explicitly set properties to their WinUI default values — it blocks future WinUI updates.

```csharp
// Wrong: these are all WinUI defaults
Button("Action")
    .Padding(12)       // Default button padding
    .CornerRadius(4)   // Default ControlCornerRadius
    .Height(32)        // Default button height

// Correct: only set what differs
Button("Action").MinHeight(40)
```

Defaults you should not set:
- Default `Foreground` on Text (it's `TextFillColorPrimaryBrush` already)
- `Padding(0)` or `Margin(0)` (zero is the default)
- `Opacity(1.0)` (1.0 is the default)
- `CornerRadius(4)` on buttons (WinUI default is `ControlCornerRadius`)

---

## Code Review Checklist

When reviewing Reactor UI code, verify:

**Theming:**
- [ ] Uses `Theme.*` tokens for colors/brushes — no hardcoded hex on themed surfaces
- [ ] Resource keys end in `Brush` (not Color name)
- [ ] No opacity on elements in High Contrast
- [ ] Acrylic surfaces use correct background + border pairings
- [ ] `BackgroundSizing = InnerBorderEdge` set on bordered acrylic containers
- [ ] No partial theme changes — Light/Dark `.Resources()` updates tested with HC in the same change
- [ ] Interactive containers (cards, list items) have visible borders in HC

**Typography:**
- [ ] Uses semantic text factories (`Heading`, `SubHeading`, `Caption`) or WinUI style tokens
- [ ] `FontWeight` is SemiBold (600), not Bold (700) — except `Heading()` page titles
- [ ] No fixed heights on text containers — uses `MinHeight`
- [ ] Trimmed text has a tooltip for overflow content
- [ ] Icons and text top-aligned in wrapping scenarios

**Layout:**
- [ ] Layout values use multiples of 4
- [ ] Corner radius is 4 (controls) or 8 (overlays) — no non-standard values
- [ ] Uses `MinHeight`/`MinWidth` instead of fixed sizing for text containers
- [ ] Uses `Border` for single-child containers (not `VStack`/`Grid` wrappers)
- [ ] No unnecessary wrapper containers without layout or styling purpose
- [ ] `HStack` does not contain text that needs `TextTrimming`
- [ ] Text trimming columns use `"*"` (not `"Auto"`, which also prevents trimming)
- [ ] `ThemeShadow` has `Translation(0, 0, 32)` and 12px parent padding
- [ ] ScrollView content uses `HorizontalContentAlignment = Stretch`

**Controls and Styling:**
- [ ] `.Resources()` used for button visual state overrides (not `.Background()` directly)
- [ ] All visual states covered when overriding — rest + hover + pressed + disabled
- [ ] No explicit setting of WinUI default values
- [ ] No no-op `.Resources()` overrides that repeat WinUI defaults
- [ ] Uses existing WinUI styles before creating custom overrides

**Accessibility:**
- [ ] `AutomationName` set on icon-only controls
- [ ] `HeadingLevel` set on heading text
- [ ] `PositionInSet` / `SizeOfSet` set on list items
- [ ] Hit-test targets for light-dismiss are visible (`Background("#00000000")`)

**State and Reconciliation:**
- [ ] `.WithKey()` set on items in dynamic lists
- [ ] Hooks are unconditional and in consistent order
- [ ] `UseCallback` wraps handlers passed to child components
- [ ] `UseMemo` wraps expensive computations

**PR Hygiene:**
- [ ] PR scope excludes unrelated churn — every change maps to a concrete UX reason
- [ ] No broad `.Resources()` or styling edits unrelated to the feature being changed

**If changing colors:** Test in NightSky HC theme, hover on interactive elements.

**If changing text/containers:** Test with scaled text and long strings.

**If changing layout:** Test at 100%, 150%, 200%, 250% display scaling.

---

## Testing Guidance

- Test Light, Dark, and High Contrast themes (especially NightSky).
- Test hover/pressed states on all interactive elements.
- Test at 100%, 150%, 200%, and 250% display scaling.
- Test with text scaling and long/localized strings for clipping and trimming.
- Verify acrylic pairings and shadow clipping after layout changes.
- Validate Figma implementation measurements at 100% scale factor.
- Capture before/after screenshots for visual changes including Light/Dark/HC evidence.

---

## References

Consult these for detailed guidance:
- [Theme-aware resources](design-docs/theme-aware-resources.md)
- [Typography and colors](design-docs/typography-and-colors.md)
- [Layout and scaling](design-docs/layout-and-scaling.md)
- [Control styles](design-docs/control-styles.md)
- [Code review checklist](design-docs/code-review-checklist.md)

## External References

- [Microsoft Design Guidelines](https://learn.microsoft.com/en-us/windows/apps/design/guidelines-overview)
- [Color in Windows](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color)
- [Typography in Windows](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)
- [WinUI 3 Gallery App](https://apps.microsoft.com/detail/9P3JFPWWDZRC)
- [WinUI Button Theme Resources](https://github.com/microsoft/microsoft-ui-xaml/blob/winui2/main/dev/CommonStyles/Button_themeresources.xaml)
- [WinUI TextBlock Theme Resources](https://github.com/microsoft/microsoft-ui-xaml/blob/winui2/main/dev/CommonStyles/TextBlock_themeresources.xaml)
- [WinUI Common Theme Resources](https://github.com/microsoft/microsoft-ui-xaml/blob/winui2/main/dev/CommonStyles/Common_themeresources_any.xaml)
