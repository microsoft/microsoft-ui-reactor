# Theme-Aware Resources in Reactor

This document covers how to apply Windows 11 theme resources correctly in Reactor's C# projection.

## How Theme Resolution Works in Reactor

WinUI provides 200+ semantic theme resources organized by theme variant (Light, Dark, HighContrast). In XAML you'd use `{ThemeResource}` markup — in Reactor, you use `Theme.*` tokens or `Theme.Ref("ResourceKey")`.

When the reconciler mounts or updates an element with a `ThemeRef`, it resolves the resource by walking `Application.Current.Resources.ThemeDictionaries` to find the brush matching the element's effective theme. When the system theme changes, elements using `ThemeRef` automatically update.

## Theme Token Quick Reference

### Text Fill

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.PrimaryText` | `TextFillColorPrimaryBrush` | Primary text (default) |
| `Theme.SecondaryText` | `TextFillColorSecondaryBrush` | Secondary text |
| `Theme.TertiaryText` | `TextFillColorTertiaryBrush` | Placeholder text |
| `Theme.DisabledText` | `TextFillColorDisabledBrush` | Disabled text |
| `Theme.AccentText` | `AccentTextFillColorPrimaryBrush` | Accent-colored text |

Do **not** set `Theme.PrimaryText` explicitly on `TextBlock()` — it is the default foreground.

### Text on Accent

| WinUI Resource Key | Purpose |
|-------------------|---------|
| `TextOnAccentFillColorPrimaryBrush` | Primary text on accent background |
| `TextOnAccentFillColorSecondaryBrush` | Secondary text on accent background |
| `TextOnAccentFillColorDisabledBrush` | Disabled text on accent background |

Use via `Theme.Ref("TextOnAccentFillColorPrimaryBrush")`.

### Accent Fill

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.Accent` | `AccentFillColorDefaultBrush` | Default accent |
| `Theme.AccentSecondary` | `AccentFillColorSecondaryBrush` | Hover state |
| `Theme.AccentTertiary` | `AccentFillColorTertiaryBrush` | Pressed state |
| `Theme.AccentDisabled` | `AccentFillColorDisabledBrush` | Disabled state |

### Control Fill

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.ControlFill` | `ControlFillColorDefaultBrush` | Control background |
| `Theme.ControlFillSecondary` | `ControlFillColorSecondaryBrush` | Hover background |
| `Theme.ControlFillTertiary` | `ControlFillColorTertiaryBrush` | Pressed background |
| `Theme.ControlFillDisabled` | `ControlFillColorDisabledBrush` | Disabled background |
| `Theme.ControlFillInputActive` | `ControlFillColorInputActiveBrush` | Focused input |

### Subtle Fill

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.SubtleFill` | `SubtleFillColorTransparentBrush` | Transparent resting state |
| — | `SubtleFillColorSecondaryBrush` | Subtle hover |
| — | `SubtleFillColorTertiaryBrush` | Subtle pressed |

### Surface and Card

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.CardBackground` | `CardBackgroundFillColorDefaultBrush` | Card background |
| `Theme.LayerFill` | `LayerFillColorDefaultBrush` | Layer background |
| `Theme.SolidBackground` | `SolidBackgroundFillColorBaseBrush` | Solid page background |
| `Theme.SmokeFill` | `SmokeFillColorDefaultBrush` | Smoke overlay |

### Stroke / Border

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.CardStroke` | `CardStrokeColorDefaultBrush` | Card border |
| `Theme.SurfaceStroke` | `SurfaceStrokeColorDefaultBrush` | Surface border |
| `Theme.DividerStroke` | `DividerStrokeColorDefaultBrush` | Dividers |
| `Theme.ControlStroke` | `ControlStrokeColorDefaultBrush` | Control border |
| `Theme.ControlStrokeSecondary` | `ControlStrokeColorSecondaryBrush` | Secondary border |

### Signal / Status

| Reactor Token | WinUI Resource Key | Purpose |
|------------|-------------------|---------|
| `Theme.SystemSuccess` | `SystemFillColorSuccessBrush` | Success |
| `Theme.SystemCaution` | `SystemFillColorCautionBrush` | Warning |
| `Theme.SystemCritical` | `SystemFillColorCriticalBrush` | Error |
| `Theme.SystemAttention` | `SystemFillColorAttentionBrush` | Information |
| `Theme.SystemNeutral` | `SystemFillColorNeutralBrush` | Neutral |

Background variants: `Theme.SystemSuccessBackground`, `Theme.SystemCautionBackground`, `Theme.SystemCriticalBackground`, `Theme.SystemAttentionBackground`, `Theme.SystemNeutralBackground`.

### Accent Colors

| WinUI Resource Key | Purpose |
|-------------------|---------|
| `SystemAccentColor` | User-chosen accent color |
| `SystemAccentColorLight1` through `Light3` | Progressively lighter accent |
| `SystemAccentColorDark1` through `Dark3` | Progressively darker accent |

Use via `Theme.Ref("SystemAccentColor")`. These are `Color` resources (not `Brush`); wrap in a brush when needed.

### Acrylic

| WinUI Resource Key | Purpose |
|-------------------|---------|
| `AcrylicBackgroundFillColorDefaultBrush` | Flyout/tooltip acrylic |
| `AcrylicBackgroundFillColorBaseBrush` | UI surface acrylic |
| `SurfaceStrokeColorFlyoutBrush` | Flyout border on acrylic |
| `SurfaceStrokeColorDefaultBrush` | Surface border on acrylic |
| `LayerOnAcrylicFillColorDefaultBrush` | Overlay on acrylic |

Use via `Theme.Ref("AcrylicBackgroundFillColorDefaultBrush")`.

## Acrylic Surface Pairings

Acrylic backgrounds have **specific** border pairings. Mixing them produces incorrect visuals.

| Surface Type | Background | Border |
|--------------|------------|--------|
| Flyouts, tooltips | `AcrylicBackgroundFillColorDefaultBrush` | `SurfaceStrokeColorFlyoutBrush` |
| UI surfaces (panels, sidebars) | `AcrylicBackgroundFillColorBaseBrush` | `SurfaceStrokeColorDefaultBrush` |

```csharp
// Flyout acrylic pairing
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

// UI surface acrylic pairing
Border(content)
    .Background(Theme.Ref("AcrylicBackgroundFillColorBaseBrush"))
    .WithBorder(Theme.Ref("SurfaceStrokeColorDefaultBrush"), 1)
    .CornerRadius(8)
    .Set(b => b.BackgroundSizing = BackgroundSizing.InnerBorderEdge)
```

## High Contrast

In High Contrast mode, WinUI maps theme resources to one of 8 system color brushes. When you use `Theme.*` tokens, HC resolution happens automatically.

**Allowed HC brushes:**

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

**HC color pairings — never mix incompatible pairs:**

| Background | Foreground | Use Case |
|------------|------------|----------|
| `SystemColorWindowColor` | `SystemColorWindowTextColor` | General content |
| `SystemColorHighlightColor` | `SystemColorHighlightTextColor` | Selected/hover states |
| `SystemColorButtonFaceColor` | `SystemColorButtonTextColor` | Buttons |
| `SystemColorWindowColor` | `SystemColorGrayTextColor` | Disabled content |
| `SystemColorWindowColor` | `SystemColorHotlightColor` | Hyperlinks |

**Rules:**
- Never use opacity on elements in HC.
- Never use accent colors or regular WinUI brushes in HC-specific code paths.
- Never use gradient animations in HC.
- Use 2px border thickness in HC for flyouts, dialogs, and cards.
- **No partial theme updates** — when changing Light/Dark resource overrides, include matching HC-safe values in the same change.
- **Empty HC is valid** — when `.Resources()` overrides target only Light/Dark and WinUI defaults already satisfy HC accessibility, you do not need HC-specific overrides.

**Interactive containers in HC** — clickable cards and list items must show a visible border in HC to indicate interactivity. Use `SystemColorHighlightColor` for the border:

```csharp
// Interactive card — highlight border visible in HC
Border(cardContent)
    .Background(Theme.CardBackground)
    .WithBorder(Theme.CardStroke, 1)
    .CornerRadius(4)
    // In HC, WinUI maps CardStroke appropriately.
    // For custom interactive surfaces, test that the border is visible in NightSky.
```

**HC setup at app level:**

```csharp
Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
```

This prevents the system from applying automatic HC overrides on top of your theme dictionaries.

## ARGB Encoding for Opacity

When a surface needs translucency in Light/Dark (but cannot use opacity in HC), encode the opacity in the alpha channel:

```csharp
// 25% opacity via alpha channel (0x40 = 64 decimal = 25%)
Border(child).Background("#40000000")
```

## Per-Subtree Theme Override

Force a subtree to Light or Dark regardless of system theme:

```csharp
// Always dark sidebar
VStack(sidebarContent)
    .RequestedTheme(ElementTheme.Dark)

// Always light content area
VStack(mainContent)
    .RequestedTheme(ElementTheme.Light)
```

## Detecting Theme Changes

Use `UseColorScheme()` to reactively respond to system theme changes:

```csharp
var scheme = UseColorScheme();

return VStack(
    TextBlock(scheme.IsDarkTheme ? "Dark Mode" : "Light Mode"),
    // Adjust layout or content based on theme
);
```

## App-Level Custom Theme Resources

Define brand colors and app-specific semantic tokens that adapt to Light, Dark, and High Contrast using `AppTheme.Register()`. This injects custom brushes into WinUI's `Application.Current.Resources.ThemeDictionaries` at app startup — making them available throughout the app via `Theme.Ref()` and lightweight styling `.Resources()`.

### Defining Custom Theme Resources

Call `AppTheme.Register()` in the App constructor or `OnLaunched`, **before** building the visual tree:

```csharp
AppTheme.Register(theme => theme
    .Add("BrandPrimaryBrush",
        light: "#005A9E",
        dark: "#4FC3F7",
        highContrast: "SystemColorHighlightColorBrush")
    .Add("BrandSecondaryBrush",
        light: "#106EBE",
        dark: "#81D4FA",
        highContrast: "SystemColorHotlightColorBrush")
    .Add("BrandSurfaceBrush",
        light: "#F0F6FF",
        dark: "#1A1A2E",
        highContrast: "SystemColorWindowColorBrush")
    .Add("BrandSubtleBrush",
        light: "#E8F0FE",
        dark: "#2A2A3E",
        highContrast: "SystemColorButtonFaceColorBrush"));
```

Under the hood, `AppTheme.Register()`:
1. Gets or creates the Light, Dark, and HighContrast `ResourceDictionary` entries in `Application.Current.Resources.ThemeDictionaries`
2. Adds a `SolidColorBrush` for each key in each theme variant
3. For HC values that reference a system brush key (e.g., `"SystemColorHighlightColorBrush"`), resolves the brush at registration time

### Consuming Custom Resources

Custom resources use the same `Theme.Ref()` API as WinUI built-in resources — no new syntax:

```csharp
// Direct property usage
Border(child).Background(Theme.Ref("BrandPrimaryBrush"))
TextBlock("Branded heading").Foreground(Theme.Ref("BrandSecondaryBrush"))

// Lightweight styling — brand button with all states
Button("Brand Action", onClick).Resources(r => r
    .Set("ButtonBackground", Theme.Ref("BrandPrimaryBrush"))
    .Set("ButtonBackgroundPointerOver", Theme.Ref("BrandSecondaryBrush"))
    .Set("ButtonBackgroundPressed", Theme.Ref("BrandPrimaryBrush"))
    .Set("ButtonForeground", Theme.Ref("TextOnAccentFillColorPrimaryBrush")))

// Brand-colored card surface
Border(
    VStack(12, cardContent).Margin(16)
)
.Background(Theme.Ref("BrandSurfaceBrush"))
.WithBorder(Theme.CardStroke, 1)
.CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
```

### Gradient Brushes

For surfaces that accept arbitrary `Brush` types (e.g., `Border.Background`), `AddGradient()` registers `LinearGradientBrush` resources. In High Contrast, gradients must fall back to a solid color.

**Important:** Most control template resource keys (e.g., `ButtonBackground`, `TextControlBackground`) expect `SolidColorBrush`. Assigning gradients to those keys may not render correctly. Use gradients only on direct surface properties.

```csharp
AppTheme.Register(theme => theme
    .AddGradient("BrandAccentGradientBrush",
        light: new GradientDef(("0", "#7cb6e9"), ("0.5", "#335fe3"), ("1.0", "#ee9bbf")),
        dark: new GradientDef(("0", "#7cb6e9"), ("0.5", "#335fe3"), ("1.0", "#ee9bbf")),
        highContrast: "#48B1E9"));  // Solid fallback in HC

// Use on surfaces, not control template keys
Border(hero).Background(Theme.Ref("BrandAccentGradientBrush"))
```

### XAML Equivalent

`AppTheme.Register()` is the Reactor equivalent of defining custom brushes in XAML's `App.xaml` or a `Colors.xaml` resource dictionary with `ThemeDictionaries`:

```xml
<!-- XAML equivalent — what AppTheme.Register() generates under the hood -->
<ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Light">
        <SolidColorBrush x:Key="BrandPrimaryBrush" Color="#005A9E" />
    </ResourceDictionary>
    <ResourceDictionary x:Key="Dark">
        <SolidColorBrush x:Key="BrandPrimaryBrush" Color="#4FC3F7" />
    </ResourceDictionary>
    <ResourceDictionary x:Key="HighContrast">
        <SolidColorBrush x:Key="BrandPrimaryBrush"
                         Color="{ThemeResource SystemColorHighlightColor}" />
    </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
```

### Rules

1. **Register before building the visual tree** — call `AppTheme.Register()` in the App constructor or `OnLaunched`, before any component renders.
2. **Always provide all three variants** (light, dark, highContrast). Omitting HC causes accessibility regressions. There is no optional-HC overload by design.
3. **HC values must reference system color brushes or solid hex colors** — no gradients, no opacity, no custom colors in HC. Use WinUI system brush keys like `"SystemColorHighlightColorBrush"`, `"SystemColorHotlightColorBrush"`, etc.
4. **Custom keys must end in `Brush`** — matches WinUI naming conventions and ensures `Theme.Ref()` resolves them correctly.
5. **Don't re-register WinUI built-in keys** — use `Theme.*` tokens or `Theme.Ref()` for existing WinUI resources. `AppTheme.Register()` is for app-defined resources only.
6. **Duplicate key registration throws** — registering the same key twice throws at startup. Libraries and app code must coordinate key names to avoid collisions.
7. **HC brush references are snapshots** — HC values that reference system brush keys are resolved at registration time. If the HC palette changes at runtime (rare), custom brushes won't update until the next full re-render.
