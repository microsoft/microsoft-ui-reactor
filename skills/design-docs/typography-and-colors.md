# Typography and Colors in Reactor

## Typography

### The Windows 11 Type Ramp

Windows 11 defines a type ramp of semantic text styles. In Reactor, use the built-in text factories for common sizes, or apply WinUI styles for the full ramp.

#### Reactor Text Factories

| Factory | Size | Weight | Use Case |
|---------|------|--------|----------|
| `Caption("text")` | 12px | Regular (400) | Small labels, timestamps, metadata |
| `TextBlock("text")` | 14px | Regular (400) | Default body text |
| `SubHeading("text")` | 20px | SemiBold (600) | Section headers, card titles |
| `Heading("text")` | 28px | Bold (700) | Page titles |

#### Full WinUI Type Ramp

For sizes not covered by the factories, apply a WinUI style:

| WinUI Style | Size | Weight | Reactor Equivalent |
|-------------|------|--------|-----------------|
| `CaptionTextBlockStyle` | 12px | Regular | `Caption()` |
| `BodyTextBlockStyle` | 14px | Regular | `TextBlock()` (default) |
| `BodyStrongTextBlockStyle` | 14px | SemiBold | `TextBlock().SemiBold()` |
| `BodyLargeTextBlockStyle` | 18px | Regular | Apply via `.Set()` |
| `SubtitleTextBlockStyle` | 20px | SemiBold | `SubHeading()` |
| `TitleTextBlockStyle` | 28px | SemiBold | `Heading()` |
| `TitleLargeTextBlockStyle` | 40px | SemiBold | Apply via `.Set()` |
| `DisplayTextBlockStyle` | 68px | SemiBold | Apply via `.Set()` |

**Applying WinUI styles in Reactor:**

```csharp
// For styles not covered by factories
TextBlock("Large title").Set(tb =>
    tb.Style = (Style)Application.Current.Resources["TitleLargeTextBlockStyle"])

TextBlock("Display text").Set(tb =>
    tb.Style = (Style)Application.Current.Resources["DisplayTextBlockStyle"])

TextBlock("Body strong").SemiBold()

// BodyLargeTextBlockStyle
TextBlock("Prominent text").Set(tb =>
    tb.Style = (Style)Application.Current.Resources["BodyLargeTextBlockStyle"])
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

4. **Icon font family** — Never hardcode `"Segoe Fluent Icons"`. Use the system resource:

   ```csharp
   TextBlock("\uE710").Set(tb =>
       tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
       .Set(tb => tb.IsTextScaleFactorEnabled = false)
   ```

5. **Icon TextBlocks should not scale with text settings:**

   ```csharp
   TextBlock("\uE710")
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
    TextBlock(paragraph).Set(tb => tb.TextWrapping = TextWrapping.WrapWholeWords)
    ```

11. **Top-align icons with text** — When icons and text are paired in wrapping layouts, prefer top alignment for both. At larger text scales, center-aligned icons drift visually:

    ```csharp
    HStack(8,
        TextBlock("\uE710")
            .Set(tb => tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
            .Set(tb => tb.IsTextScaleFactorEnabled = false)
            .VAlign(VerticalAlignment.Top),
        TextBlock(description)
            .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
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
