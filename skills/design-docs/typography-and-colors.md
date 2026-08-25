# Typography and Colors in Reactor

## Typography

### The Windows 11 Type Ramp

Windows 11 defines a type ramp of semantic text styles. In Reactor, use the built-in text factories for common sizes, or apply WinUI styles for the full ramp.

#### Reactor Text Factories

| Factory | Size | Weight | Use Case |
|---------|------|--------|----------|
| `Caption("text")` | 12px | Regular (400) | Small labels, timestamps, metadata |
| `TextBlock("text")` | 14px | Regular (400) | Default body text |
| `Body("text")` | 14px | Regular | WinUI `BodyTextBlockStyle` body text |
| `BodyStrong("text")` | 14px | SemiBold | Emphasized inline labels |
| `BodyLarge("text")` | 18px | Regular | Prominent body text |
| `Subtitle("text")` | 20px | SemiBold | WinUI `SubtitleTextBlockStyle` — section headings |
| `SubHeading("text")` | 20px | SemiBold (600) | Section headers, card titles (Reactor preset) |
| `Title("text")` | 28px | SemiBold | WinUI `TitleTextBlockStyle` — page titles |
| `Heading("text")` | 28px | Bold (700) | Page titles (Reactor preset, slightly heavier) |
| `TitleLarge("text")` | 40px | SemiBold | Primary titles on feature/landing pages |
| `Display("text")` | 68px | SemiBold | Hero banners, at most one per page |

#### Full WinUI Type Ramp

Every style in the ramp now has a matching factory:

| WinUI Style | Size | Weight | Reactor Equivalent |
|-------------|------|--------|-----------------|
| `CaptionTextBlockStyle` | 12px | Regular | `Caption()` |
| `BodyTextBlockStyle` | 14px | Regular | `Body()` |
| `BodyStrongTextBlockStyle` | 14px | SemiBold | `BodyStrong()` |
| `BodyLargeTextBlockStyle` | 18px | Regular | `BodyLarge()` |
| `SubtitleTextBlockStyle` | 20px | SemiBold | `Subtitle()` |
| `TitleTextBlockStyle` | 28px | SemiBold | `Title()` |
| `TitleLargeTextBlockStyle` | 40px | SemiBold | `TitleLarge()` |
| `DisplayTextBlockStyle` | 68px | SemiBold | `Display()` |

**Applying WinUI styles in Reactor:**

Every entry in the ramp above has a factory — prefer it. For any other named
style, use `.ApplyStyle()`, which resolves the key against the app's resources
and, when it does not resolve, keeps the element's default appearance and
reports the key on the `Microsoft-UI-Reactor` trace. Never assign
`Application.Current.Resources[...]` through `.Set()`: the indexer throws on a
missing key, and that exception escapes the mount action and fails the render.

```csharp
TitleLarge("Large title")
Display("Display text")
BodyStrong("Body strong")
BodyLarge("Prominent text")

// Any other named style:
TextBlock("Custom").ApplyStyle("MyAppTextBlockStyle")
```

### Typography Rules

1. **Use semantic factories or styles** — do not set `FontSize` and `FontWeight` directly for standard UI text.

   ```csharp
   // Correct
   Heading("Settings")
   SubHeading("General")
   Caption("Last updated: 2 hours ago")

   // Wrong
   TextBlock("Settings").FontSize(28).FontWeight(new FontWeight(700))
   ```

2. **SemiBold (600), not Bold (700)** — Bold is not part of the Windows 11 design language. Exception: `Heading()` intentionally uses 700 for page titles.

   ```csharp
   // Correct: SemiBold for emphasis
   TextBlock("Important").SemiBold()

   // Wrong: Bold
   TextBlock("Important").Bold()
   ```

3. **Minimum font size: 12px** — Anything below 12px makes complex Asian characters unreadable. `Caption()` at 12px is the smallest acceptable body text size.

4. **Icon font family** — Never hardcode a bare `"Segoe Fluent Icons"`. Use the system resource, and apply it with the `.FontFamily(FontFamily)` modifier rather than `.Set` — the modifier is diffed structurally, so an unchanged font costs nothing, and it is cleared when removed:

   ```csharp
   TextBlock("\uE710")
       .FontFamily((FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
       .Set(tb => tb.IsTextScaleFactorEnabled = false)
   ```

   Where no live `Application.Current` is available, the explicit
   `"Segoe Fluent Icons, Segoe MDL2 Assets"` stack is an acceptable static
   fallback — it keeps the Windows 10 tail that a bare `"Segoe Fluent Icons"` drops.

5. **Icon TextBlocks should not scale with text settings:**

   ```csharp
   TextBlock("\uE710")
       .FontFamily((FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
       .Set(tb => tb.IsTextScaleFactorEnabled = false)
       .VAlign(VerticalAlignment.Center)
   ```

6. **Tabular numerals for changing numbers** — Prevents width jitter on clocks, percentages, counters:

   ```csharp
   TextBlock($"{batteryPercent}%").Set(tb =>
       tb.Typography.NumeralAlignment = FontNumeralAlignment.Tabular)
   ```

7. **Text trimming requires constrained width** — `HStack` gives unbounded width so trimming never fires. Use `Grid` with a `GridSize.Star()` column (not `GridSize.Auto`, which also sizes to content and prevents trimming):

   ```csharp
   Grid(
       columns: [GridSize.Auto, GridSize.Star()],
       rows: [GridSize.Auto],
       Image(avatar).Size(32, 32).Grid(column: 0),
       TextBlock(longTitle)
           .TextTrimming(TextTrimming.CharacterEllipsis)
           .Grid(column: 1))
   ```

8. **Smart tooltips for trimmed text** — When text is trimmed, add a tooltip so the user can read the full content on hover:

   ```csharp
   TextBlock(longTitle)
       .TextTrimming(TextTrimming.CharacterEllipsis)
       .ToolTip(longTitle)
   ```

9. **Default foreground** — `TextFillColorPrimaryBrush` is the default TextBlock foreground. Do not set it explicitly.

10. **TextWrapping** — `NoWrap` is the default (do not set it explicitly). Choose `Wrap` when text should flow to multiple lines, or `WrapWholeWords` for body text to avoid mid-word breaks:

    ```csharp
    TextBlock(paragraph).TextWrapping(TextWrapping.WrapWholeWords)
    ```

11. **Top-align icons with text** — When icons and text are paired in wrapping layouts, prefer top alignment for both. At larger text scales, center-aligned icons drift visually:

    ```csharp
    HStack(8,
        TextBlock("\uE710")
            .FontFamily((FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
            .Set(tb => tb.IsTextScaleFactorEnabled = false)
            .VAlign(VerticalAlignment.Top),
        TextBlock(description)
            .TextWrapping(TextWrapping.Wrap)
            .VAlign(VerticalAlignment.Top))
    ```

## Colors

### Color Application in Reactor

Colors are applied via three mechanisms:

1. **Theme tokens** (preferred): `Theme.PrimaryText`, `Theme.Accent`, etc.
2. **Theme references**: `Theme.Ref("AnyWinUIResourceKey")`
3. **Hex strings** (escape hatch): `"#AARRGGBB"` or `"#RRGGBB"`

```csharp
// Theme token — auto-updates with theme
TextBlock("Hello").Foreground(Theme.PrimaryText)

// Theme reference — any WinUI resource
Border(child).Background(Theme.Ref("CardBackgroundFillColorDefaultBrush"))

// Hex string — fixed color, does NOT change with theme
Border(child).Background("#FF0000")  // Only for non-themed decorative elements
```

### Approved Color Resources

When building themed surfaces, use only approved WinUI brush resources. The full approved list:

#### Text Fill Colors
```
TextFillColorPrimaryBrush
TextFillColorSecondaryBrush
TextFillColorTertiaryBrush
TextFillColorDisabledBrush
TextFillColorInverseBrush
AccentTextFillColorPrimaryBrush
AccentTextFillColorSecondaryBrush
AccentTextFillColorTertiaryBrush
AccentTextFillColorDisabledBrush
TextOnAccentFillColorPrimaryBrush
TextOnAccentFillColorSecondaryBrush
TextOnAccentFillColorDisabledBrush
```

#### Control Fill Colors
```
ControlFillColorDefaultBrush
ControlFillColorSecondaryBrush
ControlFillColorTertiaryBrush
ControlFillColorQuarternaryBrush
ControlFillColorDisabledBrush
ControlFillColorTransparentBrush
ControlFillColorInputActiveBrush
ControlStrongFillColorDefaultBrush
ControlStrongFillColorDisabledBrush
ControlSolidFillColorDefaultBrush
```

#### Subtle Fill Colors
```
SubtleFillColorTransparentBrush
SubtleFillColorSecondaryBrush
SubtleFillColorTertiaryBrush
SubtleFillColorDisabledBrush
```

#### ControlAlt Fill Colors
```
ControlAltFillColorTransparentBrush
ControlAltFillColorSecondaryBrush
ControlAltFillColorTertiaryBrush
ControlAltFillColorQuarternaryBrush
ControlAltFillColorDisabledBrush
```

#### ControlOnImage Fill Colors
```
ControlOnImageFillColorDefaultBrush
ControlOnImageFillColorSecondaryBrush
ControlOnImageFillColorTertiaryBrush
ControlOnImageFillColorDisabledBrush
```

#### Accent Fill Colors
```
AccentFillColorDefaultBrush
AccentFillColorSecondaryBrush
AccentFillColorTertiaryBrush
AccentFillColorDisabledBrush
AccentFillColorSelectedTextBackgroundBrush
```

#### Stroke / Border Colors
```
ControlStrokeColorDefaultBrush
ControlStrokeColorSecondaryBrush
ControlStrokeColorOnAccentDefaultBrush
ControlStrokeColorOnAccentSecondaryBrush
ControlStrokeColorOnAccentTertiaryBrush
ControlStrokeColorOnAccentDisabledBrush
ControlStrokeColorForStrongFillWhenOnImageBrush
CardStrokeColorDefaultBrush
CardStrokeColorDefaultSolidBrush
ControlStrongStrokeColorDefaultBrush
ControlStrongStrokeColorDisabledBrush
SurfaceStrokeColorDefaultBrush
SurfaceStrokeColorFlyoutBrush
SurfaceStrokeColorInverseBrush
DividerStrokeColorDefaultBrush
FocusStrokeColorOuterBrush
FocusStrokeColorInnerBrush
```

#### Surface / Card / Layer Colors
```
CardBackgroundFillColorDefaultBrush
CardBackgroundFillColorSecondaryBrush
CardBackgroundFillColorTertiaryBrush
SmokeFillColorDefaultBrush
LayerFillColorDefaultBrush
LayerFillColorAltBrush
LayerOnAcrylicFillColorDefaultBrush
LayerOnAccentAcrylicFillColorDefaultBrush
LayerOnMicaBaseAltFillColorDefaultBrush
LayerOnMicaBaseAltFillColorSecondaryBrush
SolidBackgroundFillColorBaseBrush
SolidBackgroundFillColorSecondaryBrush
SolidBackgroundFillColorTertiaryBrush
SolidBackgroundFillColorQuarternaryBrush
SolidBackgroundFillColorQuinaryBrush
SolidBackgroundFillColorSenaryBrush
SolidBackgroundFillColorTransparentBrush
SolidBackgroundFillColorBaseAltBrush
```

#### System / Signal Colors
```
SystemFillColorSuccessBrush
SystemFillColorCautionBrush
SystemFillColorCriticalBrush
SystemFillColorNeutralBrush
SystemFillColorSolidNeutralBrush
SystemFillColorAttentionBackgroundBrush
SystemFillColorSuccessBackgroundBrush
SystemFillColorCautionBackgroundBrush
SystemFillColorCriticalBackgroundBrush
SystemFillColorNeutralBackgroundBrush
SystemFillColorSolidAttentionBackgroundBrush
SystemFillColorSolidNeutralBackgroundBrush
```

#### Accent Colors
```
SystemAccentColor
SystemAccentColorLight1
SystemAccentColorLight2
SystemAccentColorLight3
SystemAccentColorDark1
SystemAccentColorDark2
SystemAccentColorDark3
```

Note: Accent entries above are `Color` resources (not Brush). Use via `Theme.Ref("SystemAccentColor")`.

### High Contrast System Colors

In High Contrast mode, only the 8 system color brushes are allowed:

```
SystemColorWindowTextColorBrush
SystemColorWindowColorBrush
SystemColorHighlightTextColorBrush
SystemColorHighlightColorBrush
SystemColorButtonTextColorBrush
SystemColorButtonFaceColorBrush
SystemColorGrayTextColorBrush
SystemColorHotlightColorBrush
```
