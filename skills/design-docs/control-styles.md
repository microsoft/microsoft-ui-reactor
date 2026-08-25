# Control Styles in Reactor

## Lightweight Styling

In XAML, you'd override control resources in a `ResourceDictionary.ThemeDictionaries` block. In Reactor, use `.Resources()` to inject per-control resource overrides. The control's `VisualStateManager` automatically uses them for hover/pressed/disabled states.

### Subtle Button Pattern

A toolbar-style button with transparent background:

```csharp
Button("Action", onClick).Resources(r => r
    .Set("ButtonBackground", Theme.SubtleFill)
    .Set("ButtonBackgroundPointerOver", Theme.Ref("SubtleFillColorSecondaryBrush"))
    .Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush"))
    .Set("ButtonBorderBrush", Theme.SubtleFill)
    .Set("ButtonBorderBrushPointerOver", Theme.SubtleFill)
    .Set("ButtonBorderBrushPressed", Theme.SubtleFill))
```

### Accent Button Pattern

A primary action button with accent color:

```csharp
Button("Primary Action", onClick).Resources(r => r
    .Set("ButtonBackground", Theme.Accent)
    .Set("ButtonBackgroundPointerOver", Theme.AccentSecondary)
    .Set("ButtonBackgroundPressed", Theme.AccentTertiary)
    .Set("ButtonForeground", Theme.Ref("TextOnAccentFillColorPrimaryBrush"))
    .Set("ButtonForegroundPointerOver", Theme.Ref("TextOnAccentFillColorPrimaryBrush"))
    .Set("ButtonForegroundPressed", Theme.Ref("TextOnAccentFillColorSecondaryBrush")))
```

Or, if the WinUI `AccentButtonStyle` is available, use the fluent that applies it:

```csharp
Button("Primary Action", onClick).AccentButton()
```

### Why Not Just `.Background()`?

Setting `.Background()` directly on a Button overrides the resting state only. Hover, pressed, and disabled states still use the default theme — creating visual inconsistency.

```csharp
// Wrong: only changes resting state
Button("Action", onClick).Background(Theme.Accent)
// Hover/pressed/disabled states are still default gray

// Correct: all states are consistent
Button("Action", onClick).Resources(r => r
    .Set("ButtonBackground", Theme.Accent)
    .Set("ButtonBackgroundPointerOver", Theme.AccentSecondary)
    .Set("ButtonBackgroundPressed", Theme.AccentTertiary))
```

## Common Resource Keys

### Button Resources

| Key | State | Purpose |
|-----|-------|---------|
| `ButtonBackground` | Rest | Background color |
| `ButtonBackgroundPointerOver` | Hover | Hover background |
| `ButtonBackgroundPressed` | Pressed | Pressed background |
| `ButtonBackgroundDisabled` | Disabled | Disabled background |
| `ButtonForeground` | Rest | Text/icon color |
| `ButtonForegroundPointerOver` | Hover | Hover text color |
| `ButtonForegroundPressed` | Pressed | Pressed text color |
| `ButtonForegroundDisabled` | Disabled | Disabled text color |
| `ButtonBorderBrush` | Rest | Border color |
| `ButtonBorderBrushPointerOver` | Hover | Hover border |
| `ButtonBorderBrushPressed` | Pressed | Pressed border |

Consult the [WinUI Button theme resources](https://github.com/microsoft/microsoft-ui-xaml/blob/winui2/main/dev/CommonStyles/Button_themeresources.xaml) for the complete list.

### TextBox Resources

| Key | Purpose |
|-----|---------|
| `TextControlForeground` | Text color |
| `TextControlBackground` | Background |
| `TextControlBorderBrush` | Border |
| `TextControlPlaceholderForeground` | Placeholder text |
| `TextControlForegroundFocused` | Focused text |
| `TextControlBackgroundFocused` | Focused background |

### ToggleSwitch Resources

| Key | Purpose |
|-----|---------|
| `ToggleSwitchFillOn` | On state fill |
| `ToggleSwitchFillOff` | Off state fill |
| `ToggleSwitchStrokeOn` | On state border |
| `ToggleSwitchStrokeOff` | Off state border |

## Applying WinUI Styles

For built-in WinUI styles, use `.ApplyStyle()`:

```csharp
Button("Accent", onClick).AccentButton()   // dedicated fluent for this one

BodyStrong("Body strong")   // type-ramp styles have factories — prefer them

TextBlock("Custom").ApplyStyle("MyAppTextBlockStyle")   // anything else
```

`.ApplyStyle()` resolves the key against the application's resources and, when
it does not resolve, keeps the element's default appearance and names the key on
the `Microsoft-UI-Reactor` trace. Do not assign
`Application.Current.Resources[...]` through `.Set()`: that indexer *throws* on a
missing key, and the exception escapes the mount action and fails the render.

## Rules

1. **Check for existing WinUI styles first** — don't create custom resource overrides when a WinUI style already exists.
2. **Keep visual states consistent** — if overriding rest state, override hover/pressed/disabled too.
3. **ResourceKey values must end in "Brush"** — target the `SolidColorBrush`, not the `Color`:
   ```csharp
   // Correct
   .Set("ButtonBackground", Theme.Ref("ControlFillColorDefaultBrush"))
   // Wrong
   .Set("ButtonBackground", Theme.Ref("ControlFillColorDefault"))
   ```
4. **Use `.Resources()` for single-control overrides** — don't create shared style resources for one-off customizations.
5. **Don't override defaults** — if the default WinUI button style is what you want, don't set `.Resources()` at all. No-op resource overrides that repeat WinUI defaults add noise and block future WinUI updates.
6. **Don't re-declare inherited properties** — when applying a WinUI style via `.ApplyStyle()`, don't also set properties the style already defines (FontSize, FontWeight, etc.). They are redundant and block future updates.
   ```csharp
   // Wrong: FontSize and weight are already in BodyStrongTextBlockStyle
   TextBlock("Bold body").SemiBold().FontSize(14).ApplyStyle("BodyStrongTextBlockStyle")

   // Correct: the factory applies the style, which handles size and weight
   BodyStrong("Bold body")
   ```
7. **Empty HC is valid for `.Resources()` patterns** — when your overrides target only Light/Dark appearance and WinUI defaults already satisfy HC accessibility, you do not need HC-specific resource overrides. This is the common case for subtle/accent button patterns.
