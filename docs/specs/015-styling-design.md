# Reactor Styling Enhancements — Detailed Design

## Status

**Draft** — 2025-07-14.

---

## Problem Statement

The [critical review](../critical-review.md) §6 and scorecard grades Styling at **C-** and
Theming at **C+** — among the weakest scores in the framework:

> **Limited ThemeRef surface.** Only 3 brush properties (Background, Foreground,
> BorderBrush) accept `ThemeRef`; remaining properties require raw `.Set()`
> callbacks. Shape `Fill`/`Stroke`, `PlaceholderForeground`, `CaretBrush`,
> `SelectionHighlightColor` are all unreachable from the fluent DSL.

> **No caching of generated styles.** `ApplyThemeBindings()` builds an XML
> `<Style>` string, parses it via `XamlReader.Load()`, and assigns it — per
> element, per render cycle. This is correct but slow.

> **No lightweight styling.** WinUI's most powerful customization feature —
> per-control resource overrides like `ButtonBackgroundPointerOver` — has no Reactor
> surface.

> **No color-scheme hooks.** Components that need to vary non-brush content by
> theme (icons, images, text) have no reactive primitive beyond `.Set()` on the
> root element.

This spec proposes four low-risk, high-value enhancements that collectively
address the most impactful gaps without waiting for the broader theme system
revamp (tracked separately).

> **Note:** Custom Theme Definitions / `ReactorThemeResources` are actively being
> redesigned by another engineer and are explicitly out of scope here.

---

## Competitive Research

### Framework Styling Models

| Capability | SwiftUI | React (CSS-in-JS) | Jetpack Compose | WPF | Flutter | **Reactor (today)** |
|---|---|---|---|---|---|---|
| **Inline styling** | ViewModifiers | style prop / Tailwind | Modifier chain | XAML attributes | Widget params | ✅ Fluent modifiers |
| **Style composition** | `.buttonStyle()` | styled-components, `cn()` | `MaterialTheme` overrides | `BasedOn` | `Theme.copyWith()` | ❌ None |
| **Lightweight overrides** | `@Environment` | CSS custom properties | `CompositionLocalOf` | ResourceDictionary override | ThemeExtension | ❌ None |
| **Theme-reactive colors** | `Color(.primary)` | CSS vars, context | `MaterialTheme.colorScheme` | `{DynamicResource}` | `Theme.of(context)` | ⚠️ 3 properties only |
| **Visual-state variants** | Automatic per control | CSS pseudo-classes | `interactionSource` | `VisualStateManager` | `WidgetStateProperty` | ❌ Requires XAML template |
| **Color-scheme hook** | `@Environment(\.colorScheme)` | `prefers-color-scheme` | `isSystemInDarkTheme()` | `SystemParameters` | `MediaQuery.platformBrightness` | ❌ None |
| **Scoped theming** | `.environment()` | Context/Provider | `MaterialTheme {}` | Merged dictionaries | Nested `Theme` | ❌ None |
| **Style caching** | Automatic | CSSOM handles it | Composition cache | DependencyProperty cache | RenderObject cache | ❌ Re-parsed every render |

### Key Competitive Insights

**SwiftUI** leads in ergonomics: `@Environment(\.colorScheme)` gives any view
reactive access to the current theme without subscriptions. Style protocols
(`.buttonStyle()`) separate visual concerns cleanly. However, SwiftUI's
environment keys are opt-in and require boilerplate to extend.

**React + CSS** has the most mature ecosystem for scoped styling: CSS custom
properties cascade naturally, Tailwind's utility classes compose well, and
`styled-components` / CSS Modules provide encapsulation. The weakness is that CSS
is a separate language from the component model.

**Jetpack Compose** is closest to Reactor architecturally — `CompositionLocal` is
the mechanism for scoped theming and is directly analogous to WinUI's
`ResourceDictionary` tree walk. Compose's `MaterialTheme` block scopes colors,
typography, and shapes to descendants — exactly what WinUI's lightweight styling
achieves, but with type safety.

**WPF/WinUI** have the most powerful styling engine of any framework, but the
XAML ceremony is immense. Reactor's unique opportunity is to expose WinUI's styling
power through ergonomic C# APIs — **especially lightweight styling**, which no
other declarative C# framework surfaces.

**Flutter** pioneered `ThemeExtension<T>` — arbitrary typed data attached to the
theme that widgets can look up. This influenced Compose's approach and validates
the "theme as typed data, not just colors" model.

---

## Current Implementation Baseline

### ThemeRef Pipeline

```
Developer writes:  Text("Hello").Foreground(Theme.PrimaryText)
                         │
                         ▼
ElementExtensions.ModifyTheme<T>() → stores { "Foreground": ThemeRef("PrimaryText") }
on Element.ThemeBindings (IReadOnlyDictionary<string, ThemeRef>)
                         │
                         ▼
Reconciler.Mount()/Update() → detects ThemeBindings != null
                         │
                         ▼
ApplyThemeBindings(fe, bindings) →
  1. StringBuilder: <Setter Property='Foreground' Value='{ThemeResource PrimaryText}'/>
  2. Wraps in <Style TargetType='TextBlock'>...</Style>
  3. XamlReader.Load(xaml) → Style object
  4. If existing style: sets BasedOn
  5. fe.Style = style
                         │
                         ▼
WinUI resolves {ThemeResource} from ThemeDictionaries → correct brush for Light/Dark
```

### Current ThemeRef Coverage

| Property | ThemeRef support | API |
|---|---|---|
| `Background` | ✅ | `.Background(Theme.X)` |
| `Foreground` | ✅ | `.Foreground(Theme.X)` |
| `BorderBrush` | ✅ | `.WithBorder(Theme.X)` |
| `Fill` (shapes) | ❌ | `.Set(s => s.Fill = ...)` |
| `Stroke` (shapes) | ❌ | `.Set(s => s.Stroke = ...)` |
| `PlaceholderForeground` | ❌ | `.Set(tb => tb.PlaceholderForeground = ...)` |
| `CaretBrush` | ❌ | `.Set(tb => tb.CaretBrush = ...)` |
| `SelectionHighlightColor` | ❌ | `.Set(tb => tb.SelectionHighlightColor = ...)` |

### Key Code Locations

| File | What | Lines |
|---|---|---|
| `Reactor/Core/Reconciler.cs` | `ApplyThemeBindings()` | 1751–1810 |
| `Reactor/Elements/ElementExtensions.cs` | `ModifyTheme<T>()` helper | 1254–1260 |
| `Reactor/Elements/ElementExtensions.cs` | Background/Foreground/Border ThemeRef overloads | 255–312 |
| `Reactor/Core/Element.cs` | `ThemeBindings` property, `ElementModifiers` record | 63, 472–530 |
| `Reactor/Core/Theme.cs` | ~80 semantic tokens, `ThemeRef` struct | 10–33 |
| `Reactor/Elements/ThemeResource.cs` | Runtime resource lookup utilities | Full file |
| `Reactor/Elements/BrushHelper.cs` | Color parsing with cache | Full file |
| `Reactor/Hosting/ReactorHost.cs` | `AttachThemeListener` / re-render on theme change | 285–295 |

---

## Proposals Overview

| # | Feature | WinUI Interop | WinUI % | Ergonomics | Risk | Priority |
|---|---|---|---|---|---|---|
| 1 | Style Bundles | ⭐⭐⭐ | 15% | ⭐⭐⭐⭐⭐ | Low | P2 |
| **2** | **Lightweight Styling** | ⭐⭐⭐⭐⭐ | 95% | ⭐⭐⭐⭐ | Low | **P0** |
| 3 | Expanded ThemeRef | ⭐⭐⭐⭐⭐ | 90% | ⭐⭐⭐⭐ | Low | P1 |
| ~~4~~ | ~~Custom Theme Definitions~~ | — | — | — | — | *Deferred* |
| **5** | **Style Caching** | ⭐⭐⭐⭐⭐ | 95% | N/A (internal) | Very Low | **P0** |
| **6** | **UseColorScheme Hook** | ⭐⭐⭐ | 60% | ⭐⭐⭐⭐⭐ | Low | **P0** |
| 7 | Control Style Protocols | ⭐⭐ | 30–70% | ⭐⭐⭐⭐⭐ | High | P3 |
| **8** | **RequestedTheme Modifier + Pit-of-Success** | ⭐⭐⭐⭐⭐ | 95% | ⭐⭐⭐⭐ | Low | **P0** |

Items **2, 5, 6, 8** are the focus of this spec (bolded). Items 1, 3, 7 are
summarized for completeness. Item 4 is deferred to the theme system revamp.

---

## Proposal 1: Style Bundles (Summary)

Composable `Func<T, T>` style functions, similar to Tailwind's `cn()` or SwiftUI
ViewModifiers. Zero WinUI overhead — purely Reactor DSL composition.

```csharp
// Define
static T CardStyle<T>(T el) where T : Element =>
    el.Background(Theme.CardBackground)
      .CornerRadius(8)
      .Padding(16)
      .WithBorder(Theme.CardStroke);

// Use
VStack(children).Apply(CardStyle)
```

This is already possible with plain C# extension methods; the question is whether
to formalize a `StyleBundle` type. **Recommendation:** document the pattern,
don't add ceremony.

---

## Proposal 2: Lightweight Styling (Deep Dive)

### Motivation

WinUI's lightweight styling is its most unique customization feature. Every
built-in control publishes resource keys for each visual state:

```
ButtonBackground, ButtonBackgroundPointerOver, ButtonBackgroundPressed, ButtonBackgroundDisabled
ButtonForeground, ButtonForegroundPointerOver, ButtonForegroundPressed, ButtonForegroundDisabled
ButtonBorderBrush, ButtonBorderBrushPointerOver, ...
```

Setting these in a control's `FrameworkElement.Resources` dictionary overrides
them for **that control and its descendants only**. The control's VisualStateManager
automatically picks up the overrides — no template rewrite needed. This gives
per-instance visual-state customization that CSS `:hover`/`:active` pseudo-classes
provide, but with zero extra mechanism.

**No other C# declarative framework exposes this.** This is a unique competitive
advantage for Reactor because of its 1:1 WinUI mapping.

### Competitive Comparison

| Framework | Equivalent | Effort |
|---|---|---|
| **WinUI (XAML)** | `<Button.Resources><SolidColorBrush x:Key="ButtonBackground">` | 3 lines XAML |
| **CSS** | `button:hover { background: ... }` | 1 line |
| **SwiftUI** | `.buttonStyle(custom)` + full drawing code | 15+ lines |
| **Compose** | `ButtonDefaults.buttonColors(containerColor = ...)` | 1 call, but no state variants |
| **Flutter** | `ElevatedButton.styleFrom(backgroundColor: MSP(...))` | Verbose but complete |
| **Reactor (today)** | `.Set(b => b.Resources["ButtonBackground"] = new SolidColorBrush(...))` | Works but not ergonomic, not theme-reactive |

### API Design

```csharp
// ── New modifier on ElementExtensions ─────────────────────────

/// <summary>
/// Overrides WinUI lightweight styling resource keys for this element.
/// Resources cascade to descendants, and VisualStateManager-based
/// transitions (hover, pressed, disabled) automatically use the overrides.
///
/// This maps directly to FrameworkElement.Resources dictionary entries —
/// the same mechanism as XAML lightweight styling.
/// </summary>
public static T Resources<T>(this T el, Action<ResourceBuilder> configure) where T : Element
{
    var builder = new ResourceBuilder();
    configure(builder);
    return el with { ResourceOverrides = builder.Build() };
}

// ── ResourceBuilder: type-safe fluent API ─────────────────────

public class ResourceBuilder
{
    private readonly Dictionary<string, object> _resources = new();
    private readonly Dictionary<string, ThemeRef> _themeResources = new();

    // ── Brush overrides (most common) ─────────────────────────
    /// <summary>Set a resource key to a literal color (hex or named).</summary>
    public ResourceBuilder Set(string key, string color)
    {
        _resources[key] = BrushHelper.Parse(color);
        return this;
    }

    /// <summary>Set a resource key to a Brush instance.</summary>
    public ResourceBuilder Set(string key, Brush brush)
    {
        _resources[key] = brush;
        return this;
    }

    /// <summary>Set a resource key to a ThemeRef — resolves reactively on theme change.</summary>
    public ResourceBuilder Set(string key, ThemeRef themeRef)
    {
        _themeResources[key] = themeRef;
        return this;
    }

    // ── Non-brush overrides ───────────────────────────────────
    public ResourceBuilder Set(string key, double value)
    {
        _resources[key] = value;
        return this;
    }

    public ResourceBuilder Set(string key, CornerRadius value)
    {
        _resources[key] = value;
        return this;
    }

    internal ResourceOverrides Build() => new(_resources, _themeResources);
}

/// <summary>
/// Immutable snapshot of resource overrides for an element.
/// Stored on Element, applied by the reconciler.
/// </summary>
public record ResourceOverrides(
    IReadOnlyDictionary<string, object> Literals,
    IReadOnlyDictionary<string, ThemeRef> ThemeRefs
);
```

### Usage Examples

```csharp
// ── Example 1: Brand-colored button ──────────────────────────
Button("Buy Now", OnBuy)
    .Resources(r => r
        .Set("ButtonBackground", "#0078D4")
        .Set("ButtonBackgroundPointerOver", "#106EBE")
        .Set("ButtonBackgroundPressed", "#005A9E")
        .Set("ButtonForeground", "white")
        .Set("ButtonForegroundPointerOver", "white")
        .Set("ButtonForegroundPressed", "white"))

// ── Example 2: Theme-reactive overrides ──────────────────────
Button("Secondary", OnAction)
    .Resources(r => r
        .Set("ButtonBackground", Theme.SubtleBackground)
        .Set("ButtonBackgroundPointerOver", Theme.SubtleBackgroundHover)
        .Set("ButtonForeground", Theme.SecondaryText))

// ── Example 3: Scoped overrides cascade to children ──────────
// A card where all buttons inside get the accent style:
VStack(
    Text("Settings"),
    Button("Save", OnSave),
    Button("Reset", OnReset)
)
.Resources(r => r
    .Set("ButtonBackground", Theme.Accent)
    .Set("ButtonForeground", Theme.AccentText))

// ── Example 4: Composable style bundles + lightweight styling ─
static T DangerButton<T>(T el) where T : Element =>
    el.Resources(r => r
        .Set("ButtonBackground", "#D13438")
        .Set("ButtonBackgroundPointerOver", "#A4262C")
        .Set("ButtonBackgroundPressed", "#8B2023")
        .Set("ButtonForeground", "white")
        .Set("ButtonForegroundPointerOver", "white"));

Button("Delete", OnDelete).Apply(DangerButton)
```

### Reconciler Implementation

```csharp
// ── In Reconciler — new method alongside ApplyThemeBindings ──

private static void ApplyResourceOverrides(FrameworkElement fe, ResourceOverrides overrides)
{
    // Ensure Resources dictionary exists
    fe.Resources ??= new ResourceDictionary();

    // Apply literal resource values
    foreach (var (key, value) in overrides.Literals)
    {
        fe.Resources[key] = value;
    }

    // Apply ThemeRef-based resources
    // These need reactive resolution — use the same XamlReader technique
    // but targeting the Resources dictionary instead of Style
    foreach (var (key, themeRef) in overrides.ThemeRefs)
    {
        // For ThemeRef, we insert a {ThemeResource} lookup.
        // Since Resources dict values are resolved at use-time by the control's
        // VisualStateManager, we need the actual resolved brush.
        // Strategy: look up the resource from the app-level dictionary.
        if (Application.Current.Resources.TryGetValue(themeRef.ResourceKey, out var resolved))
        {
            fe.Resources[key] = resolved;
        }
    }
}

// ── Call site in Mount() and Update() ─────────────────────────

if (element.ResourceOverrides is not null && control is FrameworkElement rFe)
    ApplyResourceOverrides(rFe, element.ResourceOverrides);
```

### Element Changes

```csharp
// ── Add to Element base record ────────────────────────────────
public abstract record Element
{
    // ... existing properties ...

    /// <summary>
    /// Lightweight styling resource overrides for this element.
    /// Applied to FrameworkElement.Resources by the reconciler.
    /// </summary>
    public ResourceOverrides? ResourceOverrides { get; init; }
}
```

### WinUI Interop Characteristics

- **XAML documents containing Reactor controls**: Resources set by `.Resources()`
  are real `ResourceDictionary` entries — any XAML content nested inside will
  inherit the overrides automatically.
- **Reactor content inside XAML**: If XAML sets lightweight styling resources on a
  parent, Reactor controls rendered inside will pick them up via the normal WinUI
  resource tree walk.
- **Visual states**: Overrides are picked up by `VisualStateManager` transitions
  automatically — hover, pressed, disabled states all respect the overrides
  without any Reactor intervention.
- **Theme changes**: Literal overrides persist across theme changes. ThemeRef
  overrides are re-resolved when `ActualThemeChanged` fires (via the existing
  re-render mechanism).

### Known Lightweight Styling Key Patterns

For reference, WinUI control keys follow this pattern:

```
{ControlName}{Property}                     — default state
{ControlName}{Property}PointerOver          — hover
{ControlName}{Property}Pressed              — pressed
{ControlName}{Property}Disabled             — disabled
{ControlName}{Property}Focused              — focused (TextBox, etc.)
{ControlName}{Property}SelectedPointerOver  — selected + hover (ToggleButton)
```

Common controls and their key prefixes:

| Control | Prefix | Example keys |
|---|---|---|
| Button | `Button` | `ButtonBackground`, `ButtonBackgroundPointerOver`, `ButtonForeground` |
| TextBox | `TextControl` | `TextControlBackground`, `TextControlForegroundFocused` |
| ToggleSwitch | `ToggleSwitch` | `ToggleSwitchFillOn`, `ToggleSwitchFillOnPointerOver` |
| CheckBox | `CheckBox` | `CheckBoxCheckBackgroundFillChecked` |
| ComboBox | `ComboBox` | `ComboBoxBackground`, `ComboBoxBackgroundPointerOver` |
| Slider | `Slider` | `SliderTrackFill`, `SliderThumbBackground` |
| ListView | `ListViewItem` | `ListViewItemBackgroundSelected` |

### Risk Assessment

| Risk | Mitigation |
|---|---|
| ResourceDictionary allocation per element | Only allocate when `.Resources()` is called; most elements won't use it |
| Key names are stringly-typed | Provide intellisense via XML doc comments; consider future `ButtonResources.Background` constants |
| ThemeRef resolution timing | Literal brushes work immediately; ThemeRef resolves on mount + re-render on theme change |
| Interaction with existing Style | Resources are separate from Style — they coexist naturally |

---

## Proposal 3: Expanded ThemeRef Coverage (Summary)

Extend `GetDependencyPropertyName()` and `ModifyTheme<T>()` to support all brush
properties on common controls:

```csharp
// New overloads:
public static T Fill<T>(this T el, ThemeRef theme) where T : Element => ...
public static T Stroke<T>(this T el, ThemeRef theme) where T : Element => ...
public static T PlaceholderForeground<T>(this T el, ThemeRef theme) where T : Element => ...
public static T CaretBrush<T>(this T el, ThemeRef theme) where T : Element => ...
public static T SelectionHighlightColor<T>(this T el, ThemeRef theme) where T : Element => ...
```

Requires expanding `GetDependencyPropertyName()` switch in Reconciler.cs and
adding the corresponding setter XAML generation. Straightforward extension of the
existing pattern.

---

## Proposal 5: Style Caching (Deep Dive)

### Motivation

The current `ApplyThemeBindings()` implementation calls `XamlReader.Load()` on
every mount and update. This parses an XML string into a `Style` object — an
expensive operation that produces identical results when the same ThemeRef
bindings are applied to the same control type.

From profiling: `XamlReader.Load()` is the single most expensive call in the
theming pipeline. In a list of 100 items each with `Background(Theme.CardBackground)`,
the same XAML string `<Style TargetType='Border'><Setter Property='Background'
Value='{ThemeResource CardBackground}'/></Style>` is parsed 100 times.

### Competitive Comparison

Every framework caches compiled styles:

| Framework | Caching mechanism |
|---|---|
| CSS | Parsed stylesheets cached in CSSOM; rules shared across elements |
| SwiftUI | Resolved view modifiers cached by identity |
| Compose | Composition slot table reuses unchanged compositions |
| WPF/WinUI | `Style` objects in `ResourceDictionary` are created once, shared |
| Flutter | `ThemeData` is an immutable object, only created when theme changes |
| **Reactor** | **No caching — `XamlReader.Load()` per element per render** |

### Design

```csharp
// ── Cache in Reconciler (static, thread-safe) ────────────────

/// <summary>
/// Cache of compiled Style objects keyed by their XAML template signature.
/// A Style for TargetType='Button' with setters {Background=CardBackground,
/// Foreground=PrimaryText} produces the same Style object regardless of
/// which Button instance requests it. WinUI Style objects are frozen
/// after being applied, so sharing is safe.
///
/// Key format: "Button|Background=CardBackground|Foreground=PrimaryText"
/// </summary>
private static readonly ConcurrentDictionary<string, Style> _styleCache = new();
```

### Cache Key Structure

The cache key must uniquely identify the combination of target type and theme
resource bindings:

```csharp
private static string BuildCacheKey(string targetType, IReadOnlyDictionary<string, ThemeRef> bindings)
{
    // Sort keys for deterministic ordering
    var sb = new StringBuilder(targetType);
    foreach (var (property, themeRef) in bindings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        sb.Append('|');
        sb.Append(property);
        sb.Append('=');
        sb.Append(themeRef.ResourceKey);
    }
    return sb.ToString();
}
```

**Example keys:**
```
"Border|Background=CardBackground"
"Button|Background=AccentFillColorDefaultBrush|Foreground=TextOnAccentFillColorPrimaryBrush"
"TextBlock|Foreground=TextFillColorSecondaryBrush"
```

### Modified ApplyThemeBindings

```csharp
private static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
{
    var targetType = GetStyleTargetType(fe);
    if (targetType is null) return;

    // ── Build cache key ──────────────────────────────────────
    var cacheKey = BuildCacheKey(targetType, bindings);

    // ── Try cache first ──────────────────────────────────────
    if (!_styleCache.TryGetValue(cacheKey, out var style))
    {
        // Cache miss — build and parse XAML
        var setters = new StringBuilder();
        foreach (var (property, themeRef) in bindings)
        {
            var dp = GetDependencyPropertyName(fe, property);
            if (dp is null) continue;
            var escapedResourceKey = SecurityElement.Escape(themeRef.ResourceKey);
            setters.Append($"<Setter Property='{dp}' Value='{{ThemeResource {escapedResourceKey}}}'/>");
        }

        if (setters.Length == 0) return;

        try
        {
            var xaml =
                $"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{targetType}'>" +
                setters.ToString() +
                "</Style>";
            style = (Style)XamlReader.Load(xaml);
            _styleCache.TryAdd(cacheKey, style);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor.Theme] Failed to apply ThemeBindings: {ex.Message}");
            return;
        }
    }

    // ── Apply (with BasedOn chain if element already has a style) ─
    if (fe.Style is Style existingStyle && existingStyle.TargetType == style.TargetType)
    {
        // We can't mutate the cached style's BasedOn, so create a wrapper
        var wrapper = new Style(style.TargetType) { BasedOn = existingStyle };
        foreach (var setter in style.Setters)
            wrapper.Setters.Add(setter);
        fe.Style = wrapper;
    }
    else
    {
        fe.Style = style;
    }
}
```

### Cache Invalidation Strategy

| Event | Action | Rationale |
|---|---|---|
| Theme change (Light↔Dark) | **Clear entire cache** | `{ThemeResource}` bindings resolve to different values; WinUI re-resolves them internally via the Style's live setters, so clearing is optional but keeps memory clean |
| App restart | Automatic (static field) | Cache lives in process memory |
| New ThemeRef binding combo | Auto-populated (cache miss) | `ConcurrentDictionary.TryAdd` |

> **Important note on WinUI behavior:** `{ThemeResource}` setters inside a
> `Style` are live — WinUI's resource resolution system re-evaluates them when
> the theme changes, even on cached `Style` objects. This means theme changes
> work correctly even with cached styles. The cache clear on theme change is a
> conservative optimization to free memory from any orphaned entries, not a
> correctness requirement.

### Thread Safety

- `ConcurrentDictionary` handles concurrent reads and writes safely.
- `Style` objects are created on the UI thread (where `XamlReader.Load()` must
  run) and read on the UI thread — no cross-thread access concern.
- The cache is static because `Style` objects are thread-affine to the UI thread
  and Reactor renders on a single UI thread.

### Expected Performance Impact

| Scenario | Before | After |
|---|---|---|
| 100 items, same ThemeRef | 100 × `XamlReader.Load()` | 1 × `XamlReader.Load()` + 99 cache hits |
| 50 items, 5 distinct combos | 50 × `XamlReader.Load()` | 5 × `XamlReader.Load()` + 45 cache hits |
| Theme change | 0 (full re-render) | 1 × cache clear + re-populate on demand |
| Memory | 100 identical Style objects | 1 shared Style object + wrappers for BasedOn |

### BasedOn Chain Consideration

When an element already has a `Style` (e.g., from an implicit style or explicit
XAML style), the current code chains via `BasedOn`. With caching, we cannot
mutate the cached `Style`'s `BasedOn` (it's shared). The solution creates a thin
wrapper style per element that chains to the existing style and copies the
cached setters. This wrapper is small (no XAML parsing) and preserves the
BasedOn chain correctly.

### Risk Assessment

| Risk | Mitigation |
|---|---|
| Stale cache after theme change | WinUI resolves `{ThemeResource}` live; optional cache clear on theme change |
| Memory growth | Cache size is bounded by unique (targetType, bindings) combos — typically <20 in any app |
| Style object sharing issues | WinUI `Style` is immutable once applied; BasedOn wrapper handles per-element customization |
| Regression in BasedOn behavior | The wrapper pattern preserves the exact same setter/BasedOn semantics |

---

## Proposal 6: UseColorScheme Hook (Deep Dive)

### Motivation

Every competing framework provides a way for components to reactively observe the
current color scheme (Light/Dark/HighContrast). This enables decisions beyond
brush colors — choosing different icons, text, illustrations, or layout structures
based on the active theme.

| Framework | API | Returns |
|---|---|---|
| SwiftUI | `@Environment(\.colorScheme)` | `.light` / `.dark` |
| React | `useMediaQuery('(prefers-color-scheme: dark)')` | boolean |
| Compose | `isSystemInDarkTheme()` | boolean |
| Flutter | `Theme.of(context).brightness` | `Brightness.light` / `.dark` |
| **Reactor** | **None** — must use `.Set(fe => fe.ActualTheme)` | N/A |

### API Design

```csharp
// ── New hook in Reactor.Hooks namespace ─────────────────────────

/// <summary>
/// Returns the effective color scheme for the current component's
/// position in the element tree. Triggers re-render when the theme changes.
///
/// Unlike a global query, this respects RequestedTheme overrides —
/// if a parent sets RequestedTheme=Dark, UseColorScheme returns Dark
/// even when the system is in Light mode.
///
/// Equivalent to SwiftUI's @Environment(\.colorScheme).
/// </summary>
public static ColorScheme UseColorScheme(this RenderContext ctx)
{
    // Implementation subscribes to ReactorHost's theme-change notification.
    // The returned value reflects the effective theme at this component's
    // mount point, not the global system theme.
    return ctx.UseContext<ColorSchemeContext>().CurrentScheme;
}

/// <summary>
/// Convenience: returns true if the effective theme is Dark.
/// Common pattern: var isDark = ctx.UseIsDarkTheme();
/// </summary>
public static bool UseIsDarkTheme(this RenderContext ctx)
    => ctx.UseColorScheme() == ColorScheme.Dark;

/// <summary>
/// The effective color scheme for a position in the element tree.
/// </summary>
public enum ColorScheme
{
    Light,
    Dark,
    HighContrast
}
```

### Usage Examples

```csharp
// ── Example 1: Theme-dependent icon ──────────────────────────
public override Element Render(RenderContext ctx)
{
    var isDark = ctx.UseIsDarkTheme();
    return Image(isDark ? "Assets/logo-dark.png" : "Assets/logo-light.png");
}

// ── Example 2: Theme-dependent layout ────────────────────────
public override Element Render(RenderContext ctx)
{
    var scheme = ctx.UseColorScheme();
    var borderOpacity = scheme == ColorScheme.Dark ? 0.2 : 0.1;

    return Card(
        Text("Settings")
    ).Opacity(borderOpacity);
}

// ── Example 3: High-contrast support ─────────────────────────
public override Element Render(RenderContext ctx)
{
    var scheme = ctx.UseColorScheme();
    var showBorder = scheme == ColorScheme.HighContrast;

    return VStack(
        Text("Status: Active")
            .Foreground(Theme.SuccessText)
            .If(showBorder, el => el.WithBorder(Theme.SuccessStroke, 2))
    );
}
```

### Implementation Details

The hook needs three things:

1. **ColorSchemeContext provider** — injected by `ReactorHost` at the root of the
   element tree, updated when `ActualThemeChanged` fires.

2. **Context subscription** — using the existing `UseContext<T>()` mechanism
   (or a new lightweight equivalent if UseContext doesn't exist yet) so that
   the component re-renders when the value changes.

3. **RequestedTheme awareness** — the hook must return the *effective* theme at
   the component's position, not the global system theme. If a parent element
   sets `RequestedTheme = Dark`, all descendants should see `ColorScheme.Dark`.

```csharp
// ── ColorSchemeContext — provided at root by ReactorHost ─────────

internal class ColorSchemeContext
{
    public ColorScheme CurrentScheme { get; private set; }

    /// <summary>
    /// Called by ReactorHost when ActualThemeChanged fires.
    /// Maps WinUI's ElementTheme to our ColorScheme enum.
    /// </summary>
    public void Update(ElementTheme actualTheme)
    {
        CurrentScheme = actualTheme switch
        {
            ElementTheme.Dark => ColorScheme.Dark,
            ElementTheme.Light => ColorScheme.Light,
            _ => DetectHighContrast() ? ColorScheme.HighContrast : ColorScheme.Light,
        };
    }

    private static bool DetectHighContrast()
    {
        var settings = new Windows.UI.ViewManagement.AccessibilitySettings();
        return settings.HighContrast;
    }
}
```

```csharp
// ── ReactorHost integration ──────────────────────────────────────

// In ReactorHost.AttachThemeListener:
fe.ActualThemeChanged += (sender, _) =>
{
    var actualTheme = ((FrameworkElement)sender).ActualTheme;
    _colorSchemeContext.Update(actualTheme);
    _logger.Log(LogLevel.Debug, $"Theme changed to {actualTheme} — re-rendering");
    RequestRender();
};
```

### RequestedTheme Override Behavior

A subtlety: if a component is inside a subtree with `RequestedTheme = Dark`, the
hook should return `Dark` even if the system is in Light mode. This mirrors how
SwiftUI's `@Environment(\.colorScheme)` works — it returns the *effective* value,
not the system value.

Two implementation strategies:

**Option A: Global context + RequestedTheme walk (recommended)**
- `UseColorScheme()` checks if the component's nearest mounted `FrameworkElement`
  has a different `ActualTheme` than the global theme.
- Pros: Simple, uses existing WinUI mechanism, works with XAML-set RequestedTheme.
- Cons: Requires access to the mounted element during render (available via ctx).

**Option B: Scoped context override**
- When `RequestedTheme` modifier is applied, inject a new `ColorSchemeContext`
  for that subtree.
- Pros: Pure Reactor solution, no WinUI tree walk.
- Cons: More complex, must sync with actual WinUI theme resolution.

**Recommendation:** Option A — query the `FrameworkElement.ActualTheme` of the
component's mount point. This is one property read, guaranteed accurate, and
automatically handles arbitrarily nested `RequestedTheme` overrides whether
they come from Reactor or XAML.

### WinUI Interop Characteristics

- **Reactor component inside XAML container with RequestedTheme**: `UseColorScheme()`
  correctly returns the XAML-set theme because it reads `ActualTheme` from the
  mounted element.
- **XAML content inside Reactor subtree with RequestedTheme modifier**: WinUI handles
  this natively — no Reactor involvement needed.
- **High Contrast**: Detected via `AccessibilitySettings.HighContrast`, which
  works regardless of the element tree.

### Risk Assessment

| Risk | Mitigation |
|---|---|
| Re-render churn on theme change | Already handled — theme change triggers one re-render via existing `AttachThemeListener` |
| ActualTheme not available during first render | Default to system theme; `ActualTheme` is set by the time `Loaded` fires |
| High contrast detection accuracy | `AccessibilitySettings.HighContrast` is the documented WinUI API for this |
| Context mechanism dependency | If `UseContext<T>` isn't ready, can implement as simple `RenderContext` property |

---

## Proposal 7: Control Style Protocols (Summary)

Type-safe style protocols per control, inspired by SwiftUI's `.buttonStyle()`,
Compose's `ButtonDefaults.buttonColors()`, and Flutter's `ButtonStyle.styleFrom()`.

```csharp
Button("Save", OnSave).Style(ButtonStyles.Accent)
Button("Cancel", OnCancel).Style(ButtonStyles.Subtle)
Toggle(isOn, SetIsOn).Style(ToggleStyles.Compact)
```

**Highest competitive impact** but also **highest effort and risk**. Each control
style protocol would need a mapping layer between Reactor's typed style properties
and WinUI's underlying Style/ControlTemplate/VisualStateManager. Deferred to a
future spec pending demand and the theme system revamp.

---

## Proposal 8: RequestedTheme Modifier + Pit-of-Success (Deep Dive)

### Motivation

The [theming design spec](001-theming-design.md) §Phase 4 already proposes a
`.RequestedTheme()` modifier but it hasn't been implemented. Meanwhile, developers
use a `.Set()` workaround:

```csharp
// Current workaround (from ReactorCharting.Gallery):
.Set(b => b.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light)
```

This works but:
1. Doesn't interact with ThemeRef bindings (the `.Set()` runs after theme binding
   resolution, so the order is fragile).
2. Doesn't cascade to Reactor children's theme resolution — only affects WinUI's
   native tree walk.
3. Provides no guardrails against common mistakes like hard-coding `#FFFFFF`
   instead of using `Theme.PrimaryText`.

The "pit-of-success" dimension adds static analysis (Roslyn analyzer) to guide
developers toward theme-reactive styling and away from hard-coded colors that
break on theme change.

### Part A: RequestedTheme Modifier

#### API Design

```csharp
// ── New modifier ──────────────────────────────────────────────

/// <summary>
/// Forces a specific theme for this element and its descendants.
/// WinUI's ThemeDictionaries and VisualStateManager will resolve
/// resources using the requested theme, not the system theme.
///
/// Use cases:
/// - Dark-mode sidebar in an otherwise light app
/// - Light-mode modal over a dark background
/// - "Always dark" media player chrome
/// </summary>
public static T RequestedTheme<T>(this T el, ElementTheme theme) where T : Element
    => Modify(el, new ElementModifiers { RequestedTheme = theme });
```

#### ElementModifiers Addition

```csharp
public record ElementModifiers
{
    // ... existing properties ...

    /// <summary>
    /// Forces Light, Dark, or Default theme for this element subtree.
    /// Maps directly to FrameworkElement.RequestedTheme.
    /// </summary>
    public ElementTheme? RequestedTheme { get; init; }
}
```

#### Reconciler Changes

```csharp
// ── In ApplyModifiers (Reconciler) ────────────────────────────

// After existing modifier application:
if (m.RequestedTheme.HasValue)
{
    fe.RequestedTheme = m.RequestedTheme.Value;
}
```

> **Ordering:** `RequestedTheme` must be applied **before** `ApplyThemeBindings()`
> so that ThemeRef setters resolve using the correct theme variant. This is a key
> improvement over the `.Set()` workaround.

#### Integration with UseColorScheme

When `RequestedTheme` is set on a parent, `UseColorScheme()` in descendant
components will return the overridden theme (see Proposal 6 — it reads
`ActualTheme` from the mounted element, which WinUI updates based on
`RequestedTheme` ancestry).

#### Usage Examples

```csharp
// ── Dark sidebar, light main content ─────────────────────────
HStack(
    Sidebar().RequestedTheme(ElementTheme.Dark),
    MainContent()  // inherits system theme
)

// ── Force dark theme for media controls ──────────────────────
MediaControls()
    .RequestedTheme(ElementTheme.Dark)
    .Background(Theme.SolidBackgroundFillColorBase)

// ── Dynamic theme toggle ─────────────────────────────────────
var theme = useSystemTheme ? ElementTheme.Default : selectedTheme;
ContentArea()
    .RequestedTheme(theme)
```

### Part B: Pit-of-Success Roslyn Analyzer

#### Motivation

The most common styling bug in theme-aware apps is hard-coding colors that work
in one theme but become invisible in the other:

```csharp
// ✗ BUG: White text invisible on white background in Light theme
Text("Status").Foreground("#FFFFFF")

// ✓ CORRECT: Theme-reactive, visible in both themes
Text("Status").Foreground(Theme.PrimaryText)
```

SwiftUI, Compose, and Flutter all provide system colors as the default — making
hard-coded colors the exceptional case. Reactor should do the same with static
analysis.

#### Analyzer Rules

| Rule ID | Severity | Title | Description |
|---|---|---|---|
| **REACTOR_THEME_001** | Warning | Use ThemeRef instead of hard-coded color | A hard-coded color string or `SolidColorBrush` is passed to a property that supports `ThemeRef`. Suggest the nearest semantic token. |
| **REACTOR_THEME_002** | Info | Consider lightweight styling for visual-state overrides | A `.Set()` callback assigns a brush to a property that has a lightweight styling key equivalent. |
| **REACTOR_THEME_003** | Info | RequestedTheme modifier available | A `.Set(fe => fe.RequestedTheme = ...)` call could use the fluent `.RequestedTheme()` modifier. |

#### REACTOR_THEME_001 Implementation Sketch

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UseThemeRefAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "REACTOR_THEME_001",
        "Use ThemeRef instead of hard-coded color",
        "'{0}' accepts a ThemeRef — consider Theme.{1} instead of \"{2}\"",
        "Reactor.Styling",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override void Initialize(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        // Detect: .Background("..."), .Foreground("..."), .WithBorder("...")
        // where a ThemeRef overload exists.
        // Suggest nearest semantic token based on color value heuristics.
    }
}
```

#### REACTOR_THEME_001 Code Fix

```csharp
[ExportCodeFixProvider(LanguageNames.CSharp)]
public class UseThemeRefCodeFix : CodeFixProvider
{
    // For common hard-coded values, suggest specific tokens:
    //   "#FFFFFF" or "white" → Theme.PrimaryBackground
    //   "#000000" or "black" → Theme.PrimaryText
    //   "#0078D4"            → Theme.Accent
    // For uncommon values, suggest generic:
    //   "Consider replacing with a ThemeRef for theme compatibility"
}
```

#### REACTOR_THEME_003 Code Fix

```csharp
// Before (detected by REACTOR_THEME_003):
VStack(children).Set(b => b.RequestedTheme = ElementTheme.Dark)

// After (auto-fixed):
VStack(children).RequestedTheme(ElementTheme.Dark)
```

### WinUI Interop for RequestedTheme

- **Direct mapping**: `.RequestedTheme(ElementTheme.Dark)` maps 1:1 to
  `FrameworkElement.RequestedTheme = ElementTheme.Dark`.
- **Cascading**: WinUI handles cascading natively — all descendant elements
  (both Reactor and XAML) automatically resolve ThemeDictionaries using the
  overridden theme.
- **ActualTheme**: WinUI updates `ActualTheme` on the element and all
  descendants, which the `UseColorScheme()` hook reads.

### Risk Assessment

| Risk | Mitigation |
|---|---|
| Ordering between RequestedTheme and ThemeBindings | Apply RequestedTheme before ApplyThemeBindings in reconciler |
| Analyzer false positives | REACTOR_THEME_001 only triggers on methods with ThemeRef overloads; REACTOR_THEME_002/003 are Info severity |
| Analyzer development effort | REACTOR_THEME_003 (Set → modifier) is trivial; REACTOR_THEME_001 (suggest tokens) can start simple and grow |
| Analyzer distribution | Ship as a NuGet analyzer package referenced by the Reactor project template |

---

## Implementation Phases

### Phase 1: Style Caching (Proposal 5)

**Effort:** ~2 hours. Zero API changes. Pure perf improvement.

1. Add `ConcurrentDictionary<string, Style> _styleCache` to Reconciler
2. Add `BuildCacheKey()` method
3. Modify `ApplyThemeBindings()` to check cache before `XamlReader.Load()`
4. Handle `BasedOn` wrapper for elements with existing styles
5. Optional: clear cache on theme change in `AttachThemeListener`

**Validation:** Benchmark before/after with list of 100+ themed elements.

### Phase 2: RequestedTheme Modifier (Proposal 8A)

**Effort:** ~1 hour. Small API addition.

1. Add `ElementTheme? RequestedTheme` to `ElementModifiers`
2. Add `.RequestedTheme<T>()` extension method to `ElementExtensions`
3. Add reconciler code to apply `fe.RequestedTheme` (before `ApplyThemeBindings`)
4. Add merge logic in `ElementModifiers.Merge()`
5. Update gallery sample to use modifier instead of `.Set()`

**Validation:** Gallery sample with dark sidebar / light main content.

### Phase 3: UseColorScheme Hook (Proposal 6)

**Effort:** ~4 hours. New hook + context plumbing.

1. Define `ColorScheme` enum and `ColorSchemeContext` class
2. Add `UseColorScheme()` and `UseIsDarkTheme()` extension methods on `RenderContext`
3. Wire `ReactorHost.AttachThemeListener` to update `ColorSchemeContext`
4. Implement `ActualTheme` read from mounted element for RequestedTheme awareness
5. Add gallery sample: theme-responsive icons/illustrations

**Validation:** Component inside RequestedTheme.Dark subtree correctly reports Dark.

### Phase 4: Lightweight Styling (Proposal 2)

**Effort:** ~6 hours. New API surface + reconciler support.

1. Define `ResourceBuilder`, `ResourceOverrides` types
2. Add `ResourceOverrides?` property to `Element` base record
3. Add `.Resources<T>()` extension method
4. Add `ApplyResourceOverrides()` in Reconciler
5. Handle ThemeRef-based resources (resolve from app resources)
6. Handle cleanup on update (remove old keys, add new ones)
7. Add gallery sample: brand-colored buttons, cascading overrides

**Validation:**
- Button hover/pressed states respect overrides
- Cascading to descendant controls works
- Theme change re-resolves ThemeRef overrides

### Phase 5: Roslyn Analyzers (Proposal 8B)

**Effort:** ~8 hours. Separate analyzer project.

1. Create `Reactor.Analyzers` project (Roslyn analyzer + code fix)
2. Implement REACTOR_THEME_003 (`.Set(fe.RequestedTheme)` → `.RequestedTheme()`)
3. Implement REACTOR_THEME_001 (hard-coded color → ThemeRef suggestion) with common mappings
4. Implement REACTOR_THEME_002 (`.Set()` brush → lightweight styling suggestion)
5. Package as NuGet analyzer

**Validation:** Unit tests per analyzer rule with Roslyn test infrastructure.

---

## Scorecard Impact Assessment

| Category | Current | After Phase 1–4 | After Phase 5 |
|---|---|---|---|
| **Styling** | C- | B+ | A- |
| **Theming** | C+ | B+ | A- |

**Key improvements by category:**
- Styling: Lightweight styling (+1 full grade), style caching (perf, +0.5), expanded ThemeRef (+0.5)
- Theming: UseColorScheme hook (+0.5), RequestedTheme modifier (+0.5), pit-of-success analyzers (+0.5)

---

## Open Questions

1. **Should `.Resources()` support non-brush resources (doubles, thicknesses)?**
   Yes — WinUI lightweight styling keys include `CornerRadius`, `Thickness`, and
   `Double` values. The `ResourceBuilder` API above supports these.

2. **Should the Roslyn analyzer ship in the main Reactor package or separately?**
   Separately — analyzers add to build time and should be opt-in initially.

3. **Should `UseColorScheme` detect High Contrast automatically?**
   Yes — `AccessibilitySettings.HighContrast` is cheap to query and provides
   critical accessibility information.

4. **Cache eviction policy for style cache?**
   None needed — the cache is bounded by unique (targetType, bindings) combinations,
   which is typically < 20 per app. LRU eviction would add complexity without
   meaningful benefit.

---

## References

- [Reactor Critical Review](../critical-review.md) §6 Styling and Theming
- [Reactor Theming Design Spec](001-theming-design.md) — existing ThemeRef and phase plan
- [WinUI Lightweight Styling](https://learn.microsoft.com/en-us/windows/apps/design/style/xaml-styles#lightweight-styling)
- [SwiftUI ViewModifier](https://developer.apple.com/documentation/swiftui/viewmodifier)
- [Jetpack Compose Theming](https://developer.android.com/develop/ui/compose/designsystems/custom)
- [Flutter ThemeExtension](https://api.flutter.dev/flutter/material/ThemeExtension-class.html)
