# Reactor Theming & Styling System — Detailed Design

## Design Goals

1. **Preserve today's simple local sets** — `.Background("#red")` stays fast and unchanged
2. **Default to WinUI theming** when user doesn't specify — unset properties fall through
3. **DSL syntax for theme-resource-bound values** — values that react to theme changes
4. **Subtree theme override** — e.g., a dark-themed panel within a light-themed app
5. **App-specific custom theme resources** — Light/Dark/HC variants, used via DSL
6. **Performant** — minimize interop overhead, avoid unnecessary re-renders

---

## Architecture Overview: Three-Tier Value Model

Every visual property (Background, Foreground, etc.) can be set in one of three ways:

```
┌─────────────────────────────────────────────────────────────┐
│  Tier 3: Unset (default)                                    │
│  → WinUI's built-in style/ThemeResource handles it          │
│  → Fully theme-reactive, lightest weight                    │
│  → Example: Button("Click")  // no .Background() at all    │
├─────────────────────────────────────────────────────────────┤
│  Tier 2: Theme Token Reference (NEW)                        │
│  → Resolves from WinUI resources, re-resolves on theme Δ    │
│  → Uses Reactor's reactive re-render (like React Context)      │
│  → Example: Button("Click").Background(Theme.Accent)        │
├─────────────────────────────────────────────────────────────┤
│  Tier 1: Local Concrete Value (today's behavior)            │
│  → Direct property set, fastest, overrides everything       │
│  → Not theme-reactive (your explicit value, always)         │
│  → Example: Button("Click").Background("#FF5733")           │
└─────────────────────────────────────────────────────────────┘
```

**Key principle: explicit always wins.** If you pass a concrete brush, you get that brush
regardless of theme. If you pass a theme token, you get theme reactivity. If you pass
nothing, WinUI handles it natively.

---

## New Types

### ThemeRef — A Reference to a Theme Resource

```csharp
/// <summary>
/// A reference to a WinUI theme resource that resolves at render time
/// and re-resolves automatically when the theme changes.
/// </summary>
public readonly record struct ThemeRef(string ResourceKey)
{
    public override string ToString() => $"ThemeRef({ResourceKey})";
}
```

### Theme — Static Token Catalog + Resolution

```csharp
/// <summary>
/// Provides semantic theme tokens and custom resource references.
/// All tokens resolve from WinUI's resource system and automatically
/// re-resolve when the theme changes.
/// </summary>
public static class Theme
{
    // ── WinUI Semantic Brush Tokens ──────────────────────────────
    // Accent / Brand
    public static ThemeRef Accent            => new("AccentFillColorDefaultBrush");
    public static ThemeRef AccentSecondary   => new("AccentFillColorSecondaryBrush");
    public static ThemeRef AccentTertiary    => new("AccentFillColorTertiaryBrush");
    public static ThemeRef AccentDisabled    => new("AccentFillColorDisabledBrush");

    // Text
    public static ThemeRef PrimaryText       => new("TextFillColorPrimaryBrush");
    public static ThemeRef SecondaryText     => new("TextFillColorSecondaryBrush");
    public static ThemeRef TertiaryText      => new("TextFillColorTertiaryBrush");
    public static ThemeRef DisabledText      => new("TextFillColorDisabledBrush");
    public static ThemeRef AccentText        => new("AccentTextFillColorPrimaryBrush");

    // Surfaces / Fill
    public static ThemeRef SolidBackground   => new("SolidBackgroundFillColorBaseBrush");
    public static ThemeRef CardBackground    => new("CardBackgroundFillColorDefaultBrush");
    public static ThemeRef SmokeFill         => new("SmokeFillColorDefaultBrush");
    public static ThemeRef SubtleFill        => new("SubtleFillColorSecondaryBrush");
    public static ThemeRef LayerFill         => new("LayerFillColorDefaultBrush");

    // Stroke / Border
    public static ThemeRef CardStroke        => new("CardStrokeColorDefaultBrush");
    public static ThemeRef SurfaceStroke     => new("SurfaceStrokeColorDefaultBrush");
    public static ThemeRef DividerStroke     => new("DividerStrokeColorDefaultBrush");

    // ── Custom Resource Reference ────────────────────────────────
    /// <summary>
    /// Reference a custom theme resource by key name.
    /// The resource must be defined via ReactorApp.DefineThemeResources()
    /// or exist in the WinUI resource tree.
    /// </summary>
    public static ThemeRef Ref(string resourceKey) => new(resourceKey);
}
```

### ReactorThemeResources — Custom App-Level Theme Definitions

```csharp
/// <summary>
/// Defines custom theme resources with per-theme values.
/// Injected into WinUI's Application.Current.Resources.ThemeDictionaries.
/// </summary>
public class ReactorThemeResources
{
    internal Dictionary<string, ThemedValue> Resources { get; } = new();

    public void Add(string key, Brush light, Brush dark, Brush? highContrast = null)
    {
        Resources[key] = new ThemedValue.BrushValue(light, dark,
            highContrast ?? light);
    }

    public void Add(string key, double light, double dark, double? highContrast = null)
    {
        Resources[key] = new ThemedValue.DoubleValue(light, dark,
            highContrast ?? light);
    }

    // ... other value types as needed

    internal abstract record ThemedValue
    {
        internal record BrushValue(Brush Light, Brush Dark, Brush HighContrast) : ThemedValue;
        internal record DoubleValue(double Light, double Dark, double HighContrast) : ThemedValue;
    }
}
```

---

## DSL API Design

### Tier 1: Local Values (Unchanged)

```csharp
// Everything below works exactly as today — direct local values.
Button("Click me")
    .Background("#FF5733")
    .Foreground(myBrush)
    .CornerRadius(8)
    .Padding(12, 8)

// Pre-resolved theme resources also work (but NOT theme-reactive):
Button("Click me")
    .Background(ThemeResource.Brush("AccentFillColorDefaultBrush"))
// ↑ This resolves ONCE at render time. Won't update on theme change.
//   Use Theme.Accent instead for reactivity.
```

### Tier 2: Theme Token References (New)

```csharp
// Built-in semantic tokens — auto-resolve from WinUI resources,
// re-resolve when theme changes (Light ↔ Dark).
Button("Click me")
    .Background(Theme.Accent)
    .Foreground(Theme.PrimaryText)

Text("Subtle hint")
    .Foreground(Theme.SecondaryText)

VStack(children)
    .Background(Theme.CardBackground)
    .BorderBrush(Theme.CardStroke)

// Custom app resource reference:
Text("Branded")
    .Foreground(Theme.Ref("MyBrandAccent"))

// Mix freely — local values and theme tokens on the same element:
Button("Important")
    .Background(Theme.Accent)          // theme-reactive
    .Foreground("#FFFFFF")              // always white, regardless of theme
    .CornerRadius(20)                   // local value
```

### Tier 3: Subtree Theme Override (New)

```csharp
// ── Option A: .RequestedTheme() modifier on any container ──
// Uses WinUI's native RequestedTheme mechanism.
// All WinUI controls in the subtree switch to Dark theme.
// All Theme.* tokens in the subtree also resolve to Dark values.

VStack(
    Text("This section is dark-themed"),
    Button("Dark button"),
    TextBox("").Placeholder("Dark input")
)
.RequestedTheme(ElementTheme.Dark)
.Background(Theme.SolidBackground)  // resolves to dark surface color


// ── Option B: ThemeScope component for custom resource injection ──
// Injects custom resources at a subtree scope.

ThemeScope(
    resources: new ReactorThemeResources()
        .Add("PanelBg",   light: BrushHelper.Parse("#F5F5F5"),
                           dark:  BrushHelper.Parse("#1A1A1A"))
        .Add("PanelText", light: BrushHelper.Parse("#333333"),
                           dark:  BrushHelper.Parse("#EEEEEE")),
    content: VStack(
        Text("Custom themed panel")
            .Foreground(Theme.Ref("PanelBg")),
        Button("Action")
    ).Background(Theme.Ref("PanelBg"))
)


// ── Combine: Dark theme + custom resources ──

ThemeScope(
    requestedTheme: ElementTheme.Dark,
    resources: myCustomResources,
    content: VStack(
        Text("Dark + custom themed"),
        Button("Branded dark button")
            .Background(Theme.Ref("MyBrandAccent"))
    )
)
```

### Tier 4: App-Level Custom Theme Resources

```csharp
// At app startup — define custom resources with per-theme values.
// These are injected into Application.Current.Resources.ThemeDictionaries
// and available everywhere via Theme.Ref("key").

ReactorApp.Run<MyApp>("My App", new ReactorAppOptions
{
    ThemeResources = new ReactorThemeResources
    {
        { "MyBrandAccent",   light: "#0066FF", dark: "#4499FF" },
        { "MyBrandSecondary", light: "#8899AA", dark: "#AABBCC" },
        { "MyCardBackground", light: "#FFFFFF", dark: "#2D2D2D" },
        { "MyDivider",        light: "#E0E0E0", dark: "#404040" },
    }
});

// Then in any component:
Text("Welcome")
    .Foreground(Theme.Ref("MyBrandAccent"))

VStack(content)
    .Background(Theme.Ref("MyCardBackground"))
    .BorderBrush(Theme.Ref("MyDivider"))
```

### Tier 5: UseTheme() Hook (New)

```csharp
// For dynamic theme-dependent logic beyond just property values:

public class MyComponent : Component
{
    public override Element Render(RenderContext ctx)
    {
        var theme = ctx.UseTheme();
        // theme.IsDark, theme.IsLight, theme.IsHighContrast
        // theme.ActualTheme (ElementTheme enum)

        var icon = theme.IsDark ? "icon-light.png" : "icon-dark.png";

        return VStack(
            Image(icon),
            Text("Adaptive content")
                .Foreground(theme.IsDark
                    ? BrushHelper.Parse("#FFFFFF")
                    : BrushHelper.Parse("#000000"))
        );
    }
}

// UseTheme() subscribes the component to theme changes.
// When the OS theme changes (or a parent ThemeScope changes),
// the component automatically re-renders.
```

---

## Implementation Plan

### Phase 1: ThemeRef Type + Modifier Overloads

**ThemeRef record** — simple, zero-overhead marker type (shown above).

**New modifier overloads on ElementExtensions:**

```csharp
// New overloads that accept ThemeRef instead of Brush/value:
public static T Background<T>(this T el, ThemeRef theme) where T : Element
    => el.ModifyTheme("Background", theme);

public static T Foreground<T>(this T el, ThemeRef theme) where T : Element
    => el.ModifyTheme("Foreground", theme);

public static T BorderBrush<T>(this T el, ThemeRef theme) where T : Element
    => el.ModifyTheme("BorderBrush", theme);

// Helper that stores the ThemeRef binding on the element:
internal static T ModifyTheme<T>(this T el, string property, ThemeRef theme) where T : Element
{
    var bindings = el.ThemeBindings ?? new Dictionary<string, ThemeRef>();
    bindings[property] = theme;
    return el with { ThemeBindings = bindings };
}
```

**Element base record gets a new property:**

```csharp
public abstract record Element
{
    // ... existing properties ...

    /// <summary>
    /// Theme-resource bindings for properties. When set, the reconciler
    /// resolves from WinUI resources instead of using local values.
    /// Keys are property names ("Background", "Foreground", etc.).
    /// </summary>
    public IReadOnlyDictionary<string, ThemeRef>? ThemeBindings { get; init; }
}
```

### Phase 2: Reconciler Changes — Theme-Aware Property Application

The reconciler's `ApplyModifiers` gains theme-aware behavior:

```csharp
internal void ApplyModifiers(FrameworkElement fe, ElementModifiers? oldM,
    ElementModifiers m, Element element, Action requestRerender)
{
    var themeBindings = element.ThemeBindings;

    // Background: ThemeRef takes precedence over concrete value
    if (themeBindings?.TryGetValue("Background", out var bgRef) == true)
    {
        // Resolve theme resource for the element's effective theme
        var brush = ResolveThemeResource<Brush>(fe, bgRef.ResourceKey);
        if (brush is not null)
            SetBrushProperty(fe, "Background", brush, oldM?.Background);
    }
    else if (m.Background is not null &&
             !ReferenceEquals(m.Background, oldM?.Background))
    {
        // Existing behavior — local value
        SetBrushProperty(fe, "Background", m.Background, oldM?.Background);
    }

    // ... same pattern for Foreground, BorderBrush, etc.
}

/// <summary>
/// Resolves a theme resource respecting the element's effective theme.
/// Walks up the visual tree to find the nearest RequestedTheme override,
/// then resolves from the appropriate ThemeDictionary.
/// </summary>
private static T? ResolveThemeResource<T>(FrameworkElement fe, string key)
    where T : class
{
    // First, try the element's own resource tree
    // (catches ThemeScope-injected resources)
    if (TryResolveFromTree(fe, key, out T? result))
        return result;

    // Fall back to application resources
    var effectiveTheme = GetEffectiveTheme(fe);
    return ResolveFromAppResources<T>(key, effectiveTheme);
}

private static ElementTheme GetEffectiveTheme(FrameworkElement fe)
{
    // Walk up to find nearest explicit RequestedTheme
    var current = fe;
    while (current is not null)
    {
        if (current.RequestedTheme != ElementTheme.Default)
            return current.RequestedTheme;
        current = current.Parent as FrameworkElement;
    }
    return Application.Current.RequestedTheme == ApplicationTheme.Dark
        ? ElementTheme.Dark
        : ElementTheme.Light;
}

private static T? ResolveFromAppResources<T>(string key, ElementTheme theme)
    where T : class
{
    var themeName = theme == ElementTheme.Dark ? "Dark" : "Light";
    var resources = Application.Current.Resources;

    // Try ThemeDictionaries first
    if (resources.ThemeDictionaries.TryGetValue(themeName, out var themeDict)
        && themeDict is ResourceDictionary dict
        && dict.TryGetValue(key, out var themed)
        && themed is T typedThemed)
        return typedThemed;

    // Fall back to non-themed resources
    if (resources.TryGetValue(key, out var value) && value is T typed)
        return typed;

    return null;
}
```

### Phase 3: Theme Change Detection + Re-render

**ThemeMonitor** — subscribes to OS/app theme changes:

```csharp
internal class ThemeMonitor
{
    private ElementTheme _currentTheme;
    private readonly List<WeakReference<Action>> _subscribers = new();

    internal void Initialize(FrameworkElement rootElement)
    {
        rootElement.ActualThemeChanged += (sender, _) =>
        {
            var newTheme = sender.ActualTheme;
            if (newTheme != _currentTheme)
            {
                _currentTheme = newTheme;
                NotifySubscribers();
            }
        };
    }

    internal IDisposable Subscribe(Action onThemeChanged)
    {
        var weakRef = new WeakReference<Action>(onThemeChanged);
        _subscribers.Add(weakRef);
        return new Unsubscriber(() => _subscribers.Remove(weakRef));
    }

    private void NotifySubscribers()
    {
        // Batch: collect all re-render requests, then execute once
        foreach (var weakRef in _subscribers.ToArray())
        {
            if (weakRef.TryGetTarget(out var action))
                action();
        }
    }
}
```

**UseTheme() hook on RenderContext:**

```csharp
public ThemeInfo UseTheme()
{
    // Register this component for re-render on theme change
    // (similar to how UseState subscribes to state changes)
    var rerender = GetCurrentRerenderAction();
    _themeMonitor.Subscribe(rerender);  // weak ref, auto-cleanup

    var theme = _themeMonitor.CurrentTheme;
    return new ThemeInfo(
        ActualTheme: theme,
        IsDark: theme == ElementTheme.Dark,
        IsLight: theme == ElementTheme.Light,
        IsHighContrast: AccessibilitySettings.HighContrast
    );
}

public record ThemeInfo(
    ElementTheme ActualTheme,
    bool IsDark,
    bool IsLight,
    bool IsHighContrast
);
```

**Automatic subscription for ThemeRef properties:**

Components that use ThemeRef values don't need to call `UseTheme()` explicitly.
The reconciler detects ThemeBindings on the element tree and subscribes the
component's re-render action to theme changes automatically.

```csharp
// In the reconciler, after mounting/updating a component:
if (element.ThemeBindings?.Count > 0)
{
    // Auto-subscribe this component to theme changes
    _themeMonitor.Subscribe(requestRerender);
}
```

### Phase 4: .RequestedTheme() Modifier

```csharp
// New modifier on ElementExtensions:
public static T RequestedTheme<T>(this T el, ElementTheme theme) where T : Element
    => el.Modify(m => m with { RequestedTheme = theme });

// Add to ElementModifiers:
public record ElementModifiers
{
    // ... existing properties ...
    public ElementTheme? RequestedTheme { get; init; }
}

// Reconciler applies it:
if (m.RequestedTheme.HasValue && m.RequestedTheme != oldM?.RequestedTheme)
    fe.RequestedTheme = m.RequestedTheme.Value;
```

### Phase 5: ThemeScope Component

```csharp
/// <summary>
/// Injects custom theme resources at a subtree scope.
/// Children can reference these resources via Theme.Ref("key").
/// </summary>
public class ThemeScope : Component
{
    public ReactorThemeResources? Resources { get; init; }
    public ElementTheme? RequestedTheme { get; init; }
    public required Element Content { get; init; }

    public override Element Render(RenderContext ctx)
    {
        // Wrap content in a Border (invisible container) that carries
        // the ResourceDictionary and RequestedTheme.
        var wrapper = Border(Content)
            .OnMount(fe => InjectResources(fe));

        if (RequestedTheme.HasValue)
            wrapper = wrapper.RequestedTheme(RequestedTheme.Value);

        return wrapper;
    }

    private void InjectResources(FrameworkElement fe)
    {
        if (Resources is null) return;

        fe.Resources ??= new ResourceDictionary();

        var lightDict = new ResourceDictionary();
        var darkDict = new ResourceDictionary();

        foreach (var (key, value) in Resources.Resources)
        {
            switch (value)
            {
                case ReactorThemeResources.ThemedValue.BrushValue bv:
                    lightDict[key] = bv.Light;
                    darkDict[key] = bv.Dark;
                    break;
                case ReactorThemeResources.ThemedValue.DoubleValue dv:
                    lightDict[key] = dv.Light;
                    darkDict[key] = dv.Dark;
                    break;
            }
        }

        fe.Resources.ThemeDictionaries["Light"] = lightDict;
        fe.Resources.ThemeDictionaries["Dark"] = darkDict;
    }
}

// Static helper for ergonomic use:
public static Element ThemeScope(
    ReactorThemeResources resources,
    Element content,
    ElementTheme? requestedTheme = null)
    => new ThemeScope
    {
        Resources = resources,
        RequestedTheme = requestedTheme,
        Content = content
    };
```

### Phase 6: ReactorApp.DefineThemeResources()

```csharp
public static class ReactorApp
{
    /// <summary>
    /// Registers custom theme resources into Application.Current.Resources.
    /// Call during app initialization, before any UI is rendered.
    /// </summary>
    public static void DefineThemeResources(ReactorThemeResources resources)
    {
        var appResources = Application.Current.Resources;

        // Ensure ThemeDictionaries exist
        if (!appResources.ThemeDictionaries.ContainsKey("Light"))
            appResources.ThemeDictionaries["Light"] = new ResourceDictionary();
        if (!appResources.ThemeDictionaries.ContainsKey("Dark"))
            appResources.ThemeDictionaries["Dark"] = new ResourceDictionary();

        var lightDict = (ResourceDictionary)appResources.ThemeDictionaries["Light"];
        var darkDict  = (ResourceDictionary)appResources.ThemeDictionaries["Dark"];

        foreach (var (key, value) in resources.Resources)
        {
            switch (value)
            {
                case ReactorThemeResources.ThemedValue.BrushValue bv:
                    lightDict[key] = bv.Light;
                    darkDict[key]  = bv.Dark;
                    break;
                case ReactorThemeResources.ThemedValue.DoubleValue dv:
                    lightDict[key] = dv.Light;
                    darkDict[key]  = dv.Dark;
                    break;
            }
        }
    }
}
```

---

## How It All Fits Together — End-to-End Example

```csharp
// ══════════════════════════════════════════════════════════════
// App.cs — Define custom theme resources at startup
// ══════════════════════════════════════════════════════════════

ReactorApp.DefineThemeResources(new ReactorThemeResources
{
    { "BrandAccent",     light: "#0066FF", dark: "#4499FF" },
    { "BrandSecondary",  light: "#8899AA", dark: "#AABBCC" },
    { "CardSurface",     light: "#FFFFFF", dark: "#2D2D2D" },
    { "CardBorder",      light: "#E0E0E0", dark: "#404040" },
});

ReactorApp.Run<MainPage>("My App");


// ══════════════════════════════════════════════════════════════
// MainPage.cs — The main layout
// ══════════════════════════════════════════════════════════════

public class MainPage : Component
{
    public override Element Render(RenderContext ctx)
    {
        var (darkSidebar, setDarkSidebar) = ctx.UseState(false);
        var theme = ctx.UseTheme();  // subscribes to theme changes

        return HStack(
            // ── Sidebar: optionally dark-themed ──
            VStack(
                Text("Navigation")
                    .Foreground(Theme.PrimaryText),

                Button("Toggle sidebar theme")
                    .OnClick(() => setDarkSidebar(!darkSidebar)),

                NavLinks()
            )
            .Width(250)
            .Background(Theme.Ref("CardSurface"))  // custom theme resource
            .RequestedTheme(darkSidebar
                ? ElementTheme.Dark
                : ElementTheme.Default),            // subtree theme override

            // ── Main content: inherits app theme ──
            VStack(
                Text("Dashboard")
                    .Foreground(Theme.Ref("BrandAccent")),   // custom token

                Text($"Current theme: {theme.ActualTheme}")
                    .Foreground(Theme.SecondaryText),         // built-in token

                // Mix concrete + theme values freely:
                Button("Primary Action")
                    .Background(Theme.Accent)                 // theme-reactive
                    .Foreground("#FFFFFF")                     // always white
                    .CornerRadius(20)                          // concrete value
                    .Padding(16, 8),

                // Scoped custom resources for a card section:
                ThemeScope(
                    resources: new ReactorThemeResources
                    {
                        { "CardHighlight", light: "#FFF3E0", dark: "#3E2723" }
                    },
                    content: VStack(
                        Text("Special card")
                            .Foreground(Theme.PrimaryText),
                        Text("With custom highlight")
                    ).Background(Theme.Ref("CardHighlight"))
                )
            ).Padding(24)
        );
    }
}
```

---

## Scenario 1: Application-Wide Custom Theme (Lime Green Accent)

This scenario shows how to define a complete application theme that overrides WinUI's
built-in accent and surface colors. Every standard WinUI control (Button, TextBox,
ComboBox, ToggleSwitch, etc.) picks up the new colors automatically — no per-control
styling needed.

```csharp
// ══════════════════════════════════════════════════════════════
// LimeTheme.cs — Define the lime green application theme
// ══════════════════════════════════════════════════════════════

public static class LimeTheme
{
    // The key insight: WinUI controls reference named resources like
    // "AccentFillColorDefaultBrush" via {ThemeResource} in their templates.
    // By overriding THOSE EXACT KEYS in the app's ThemeDictionaries,
    // every built-in control automatically picks up the new colors.

    public static ReactorThemeResources Create()
    {
        var resources = new ReactorThemeResources();

        // ── Accent / Highlight colors ────────────────────────────
        // These are the keys that Button, ToggleSwitch, Slider, etc. use
        // for their "accent" state (checked, selected, primary action).
        resources.Add("AccentFillColorDefaultBrush",
            light: "#4CAF50",     // lime green
            dark:  "#8BC34A");    // lighter lime for dark backgrounds

        resources.Add("AccentFillColorSecondaryBrush",
            light: "#66BB6A",     // lighter on hover
            dark:  "#9CCC65");

        resources.Add("AccentFillColorTertiaryBrush",
            light: "#388E3C",     // darker on press
            dark:  "#689F38");

        resources.Add("AccentFillColorDisabledBrush",
            light: "#C8E6C9",
            dark:  "#33691E");

        // ── Accent text (text on accent-colored surfaces) ────────
        resources.Add("TextOnAccentFillColorPrimaryBrush",
            light: "#FFFFFF",
            dark:  "#000000");    // dark text on bright lime in dark mode

        resources.Add("AccentTextFillColorPrimaryBrush",
            light: "#2E7D32",     // green tinted text
            dark:  "#A5D6A7");

        // ── Control surfaces that reference accent ───────────────
        // ToggleSwitch, CheckBox, RadioButton fill when checked
        resources.Add("ToggleSwitchFillOnRestBrush",
            light: "#4CAF50",
            dark:  "#8BC34A");

        // ── App-specific brand resources (not WinUI built-in) ────
        // These are NEW resources unique to your app.
        resources.Add("BrandGradientStart",
            light: "#4CAF50",
            dark:  "#1B5E20");

        resources.Add("BrandGradientEnd",
            light: "#8BC34A",
            dark:  "#4CAF50");

        resources.Add("BrandSubtle",
            light: "#E8F5E9",     // very light green tint
            dark:  "#1B3D1B");    // very dark green tint

        return resources;
    }
}


// ══════════════════════════════════════════════════════════════
// App.cs — Apply the theme at startup
// ══════════════════════════════════════════════════════════════

// Register the theme. This injects all resources into
// Application.Current.Resources.ThemeDictionaries BEFORE
// any UI is created, so all controls resolve the new values.
ReactorApp.DefineThemeResources(LimeTheme.Create());

ReactorApp.Run<MainPage>("Lime App");


// ══════════════════════════════════════════════════════════════
// MainPage.cs — All standard controls now use lime green
// ══════════════════════════════════════════════════════════════

public class MainPage : Component
{
    public override Element Render(RenderContext ctx)
    {
        var (isChecked, setChecked) = ctx.UseState(false);
        var (sliderVal, setSliderVal) = ctx.UseState(50.0);
        var (text, setText) = ctx.UseState("");

        return VStack(
            Text("Lime Green Theme Demo")
                .FontSize(28)
                .Foreground(Theme.AccentText),  // green-tinted heading

            // ── Standard Button: automatically lime green ──
            // No explicit color needed — the default Button template uses
            // {ThemeResource AccentFillColorDefaultBrush} which we overrode.
            Button("Primary Action"),
            // ↑ This button is lime green. Hover is lighter, press is darker.
            //   We never touched .Background() — WinUI's template just works.

            // ── Accent-styled button (uses AccentButtonStyle) ──
            Button("Accent Button").ApplyStyle("AccentButtonStyle"),
            // ↑ Also lime green, because AccentButtonStyle references the
            //   same AccentFillColor* resources.

            // ── ToggleSwitch: checked state is lime green ──
            ToggleSwitch(isChecked)
                .OnToggled(setChecked),

            // ── Slider: track fill is lime green ──
            Slider(sliderVal, 0, 100)
                .OnValueChanged(setSliderVal),

            // ── TextBox: focus border is lime green ──
            TextBox(text)
                .OnTextChanged(setText)
                .Placeholder("Type here — focus border is lime"),

            // ── Explicit use of brand resources ──
            VStack(
                Text("Brand surface")
                    .Foreground(Theme.PrimaryText)
            )
            .Background(Theme.Ref("BrandSubtle"))   // custom: light green tint
            .Padding(16)
            .CornerRadius(8)

        ).Padding(24).Gap(12);
    }
}
```

**How this works under the covers:**

1. `ReactorApp.DefineThemeResources()` injects brushes into
   `Application.Current.Resources.ThemeDictionaries["Light"]` and `["Dark"]`
   under the exact keys WinUI controls expect (e.g., `"AccentFillColorDefaultBrush"`).

2. When WinUI's Button template evaluates `{ThemeResource AccentFillColorDefaultBrush}`,
   it walks the resource tree: control → parent → ... → Application. It finds our
   overridden brush in the Application's ThemeDictionaries.

3. Standard controls get lime green without Microsoft.UI.Reactor (Reactor) setting ANY local values. The properties
   stay at Tier 3 (unset), so WinUI's full style system — including PointerOver, Pressed,
   Disabled visual states — works perfectly.

4. When the OS switches Light ↔ Dark, WinUI re-resolves all ThemeResource references
   and picks up the appropriate variant from our ThemeDictionaries. No Reactor re-render needed
   for the built-in controls.

5. Custom resources (`"BrandSubtle"`, etc.) referenced via `Theme.Ref(...)` use Tier 2
   (ThemeRef), so Reactor re-renders those components on theme change to re-resolve the value.

---

## Scenario 2: Custom Reactor Component with Theme-Aware Properties

This scenario shows how to author a reusable Reactor component — a `StatusCard` — that
has multiple properties defaulting to semantic theme resources. The component looks
correct in both Light and Dark modes without any configuration, but can be customized.

```csharp
// ══════════════════════════════════════════════════════════════
// StatusCard.cs — A reusable theme-aware component
// ══════════════════════════════════════════════════════════════

/// <summary>
/// A card that displays a status icon, title, description, and optional action.
/// All colors default to semantic theme tokens, so it adapts to Light/Dark/HC
/// automatically. Consumers can override any color via props.
/// </summary>
public class StatusCard : Component
{
    // ── Props (set by consumer) ──
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? IconGlyph { get; init; }
    public Action? OnAction { get; init; }
    public string? ActionLabel { get; init; }
    public StatusKind Status { get; init; } = StatusKind.Info;

    // ── Optional style overrides (null = use theme default) ──
    public ThemeRef? BackgroundOverride { get; init; }
    public ThemeRef? AccentOverride { get; init; }
    public ThemeRef? TextOverride { get; init; }

    public override Element Render(RenderContext ctx)
    {
        // Resolve the accent color for this status kind.
        // Each maps to a semantic WinUI resource or custom resource.
        var (accentRef, iconColor) = Status switch
        {
            StatusKind.Success => (Theme.Ref("SystemFillColorSuccessBrush"),
                                   Theme.Ref("SystemFillColorSuccessBrush")),
            StatusKind.Warning => (Theme.Ref("SystemFillColorCautionBrush"),
                                   Theme.Ref("SystemFillColorCautionBrush")),
            StatusKind.Error   => (Theme.Ref("SystemFillColorCriticalBrush"),
                                   Theme.Ref("SystemFillColorCriticalBrush")),
            _                  => (Theme.Accent,
                                   Theme.Accent),
        };

        // Allow consumer overrides to take precedence
        var bg     = BackgroundOverride ?? Theme.CardBackground;
        var accent = AccentOverride ?? accentRef;
        var text   = TextOverride ?? Theme.PrimaryText;

        return HStack(
            // ── Left accent bar ──
            Border(null)
                .Width(4)
                .Background(accent)
                .CornerRadius(2),

            // ── Content ──
            VStack(
                // Title row with optional icon
                HStack(
                    IconGlyph is not null
                        ? FontIcon(IconGlyph)
                            .Foreground(iconColor)
                            .FontSize(20)
                        : null,

                    Text(Title)
                        .FontSize(16)
                        .FontWeight(FontWeights.SemiBold)
                        .Foreground(text)
                ).Gap(8),

                // Description
                Text(Description)
                    .Foreground(Theme.SecondaryText)  // always secondary text
                    .FontSize(13),

                // Optional action button
                OnAction is not null
                    ? Button(ActionLabel ?? "Action")
                        .OnClick(OnAction)
                        .Margin(0, 8, 0, 0)
                    : null
            )
            .Gap(4)
            .Padding(12, 8)

        )
        .Background(bg)
        .BorderBrush(Theme.CardStroke)    // semantic: adapts to Light/Dark
        .BorderThickness(1)
        .CornerRadius(8)
        .Padding(0)
        .Gap(0);
    }
}

public enum StatusKind { Info, Success, Warning, Error }


// ══════════════════════════════════════════════════════════════
// Usage — Default theme colors (adapts to Light/Dark automatically)
// ══════════════════════════════════════════════════════════════

public class NotificationsPage : Component
{
    public override Element Render(RenderContext ctx)
    {
        return VStack(

            // Uses ALL default theme colors — works in Light and Dark mode:
            new StatusCard
            {
                Title = "Deployment complete",
                Description = "v2.4.1 deployed to production successfully.",
                IconGlyph = "\uE73E",  // checkmark
                Status = StatusKind.Success,
            },

            new StatusCard
            {
                Title = "High memory usage",
                Description = "Server node-03 is at 92% memory.",
                IconGlyph = "\uE7BA",  // warning
                Status = StatusKind.Warning,
                OnAction = () => NavigateTo("diagnostics"),
                ActionLabel = "View diagnostics",
            },

            new StatusCard
            {
                Title = "Build failed",
                Description = "CI pipeline 'main' failed at test step.",
                IconGlyph = "\uE711",  // cancel
                Status = StatusKind.Error,
            },

            // ── Override specific colors for a branded card ──
            new StatusCard
            {
                Title = "New feature available",
                Description = "Try the new dashboard layout.",
                Status = StatusKind.Info,
                // Override the background to use our app's brand subtle color:
                BackgroundOverride = Theme.Ref("BrandSubtle"),
                AccentOverride = Theme.Ref("BrandAccent"),
            },

        ).Gap(12).Padding(24);
    }
}
```

**Key patterns demonstrated:**

1. **Props with ThemeRef defaults** — The component defines optional `ThemeRef?` override
   props. When null, it falls back to semantic theme tokens (`Theme.CardBackground`,
   `Theme.PrimaryText`, etc.). This means the component looks correct out-of-the-box
   in any theme, but consumers can customize per-instance.

2. **Status-to-token mapping** — The `StatusKind` enum maps to WinUI's built-in
   system status brushes (`SystemFillColorSuccessBrush`, etc.), which have correct
   Light/Dark/HighContrast values in WinUI's resource system.

3. **Mixed token sources** — The component uses both built-in WinUI tokens
   (`Theme.Accent`, `Theme.CardStroke`) and potentially custom app tokens
   (`Theme.Ref("BrandAccent")`). They all resolve through the same system.

4. **Composability** — The consumer can embed `StatusCard` inside a `ThemeScope`
   or a `.RequestedTheme(Dark)` container, and all theme tokens automatically
   resolve to the correct theme variant.

---

## Scenario 3: Surface Themes (Always-Dark / Always-Light Sections)

This scenario shows how to create regions of the UI that are pinned to a specific
theme regardless of the OS setting — common for media apps (dark player controls),
settings panels, or "hero" sections.

```csharp
// ══════════════════════════════════════════════════════════════
// MediaPlayer.cs — Always-dark player controls over video
// ══════════════════════════════════════════════════════════════

public class MediaPlayerPage : Component
{
    public override Element Render(RenderContext ctx)
    {
        var (isPlaying, setPlaying) = ctx.UseState(false);
        var (volume, setVolume) = ctx.UseState(0.8);
        var (progress, setProgress) = ctx.UseState(0.35);

        return VStack(

            // ── Video area: ALWAYS DARK ──
            // Even if the OS is in Light mode, the video player region
            // uses dark theme. This affects ALL descendants:
            // - WinUI built-in controls (Button, Slider) use dark styles
            // - Theme.* tokens resolve to dark variant
            // - Theme.PrimaryText → white text, Theme.SolidBackground → dark surface
            Grid(
                // Video content would go here
                Border(null)
                    .Background("#000000")
                    .MinHeight(400),

                // Transport controls overlay — pinned to dark theme
                VStack(
                    // Progress bar
                    Slider(progress * 100, 0, 100)
                        .OnValueChanged(v => setProgress(v / 100)),

                    HStack(
                        Button(isPlaying ? "\uE769" : "\uE768")  // pause/play
                            .OnClick(() => setPlaying(!isPlaying)),

                        Text($"{FormatTime(progress * 180)} / 3:00")
                            .Foreground(Theme.SecondaryText),  // → light gray (dark theme)

                        Spacer(),

                        // Volume control
                        FontIcon("\uE767").Foreground(Theme.PrimaryText),
                        Slider(volume * 100, 0, 100)
                            .OnValueChanged(v => setVolume(v / 100))
                            .Width(120),

                        Button("\uE740")  // fullscreen icon
                    )
                    .Gap(8)
                    .VerticalAlignment(VerticalAlignment.Center)
                )
                .Background(Theme.Ref("MediaControlOverlay"))
                .Padding(12, 8)
                .VerticalAlignment(VerticalAlignment.Bottom)

            ).RequestedTheme(ElementTheme.Dark),
            // ↑ Everything inside this Grid is dark-themed.
            //   The Slider, Buttons, and Text all use dark-mode colors.
            //   No explicit color overrides needed on any control.

            // ── Below the player: follows OS theme (Light or Dark) ──
            VStack(
                Text("Up Next")
                    .FontSize(20)
                    .Foreground(Theme.PrimaryText),  // adapts to OS theme

                // These cards follow the OS theme
                new MediaCard { Title = "Episode 5", Subtitle = "The journey continues" },
                new MediaCard { Title = "Episode 6", Subtitle = "A new beginning" },
            )
            .Padding(24)
            .Gap(12)

        );
    }

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
}


// ══════════════════════════════════════════════════════════════
// SettingsPage.cs — Mixed surface themes
// ══════════════════════════════════════════════════════════════

public class SettingsPage : Component
{
    public override Element Render(RenderContext ctx)
    {
        var (themePref, setThemePref) = ctx.UseState("system");

        return HStack(

            // ── Left nav: ALWAYS LIGHT ──
            // Pinned to light theme for a clean sidebar regardless of OS theme.
            VStack(
                Text("Settings")
                    .FontSize(18)
                    .FontWeight(FontWeights.SemiBold)
                    .Foreground(Theme.PrimaryText),   // → dark text (light theme)

                NavItem("General",   "\uE713", true),
                NavItem("Account",   "\uE77B", false),
                NavItem("Privacy",   "\uE72E", false),
                NavItem("Display",   "\uE7F4", false),
                NavItem("About",     "\uE946", false),
            )
            .Width(240)
            .Background(Theme.LayerFill)              // → light fill (light theme)
            .Padding(16, 24)
            .Gap(4)
            .RequestedTheme(ElementTheme.Light),       // ← ALWAYS LIGHT


            // ── Main content: OS theme with user override ──
            VStack(
                Text("Display Settings")
                    .FontSize(24)
                    .Foreground(Theme.PrimaryText),

                // Theme selector — user can choose system/light/dark
                Text("App theme").Foreground(Theme.SecondaryText),
                RadioButtons(
                    ("System default", "system"),
                    ("Light",          "light"),
                    ("Dark",           "dark")
                ).Selected(themePref).OnSelectionChanged(setThemePref),

                // ── Preview pane: shows what the selected theme looks like ──
                Text("Preview").Foreground(Theme.SecondaryText).Margin(0, 16, 0, 4),

                VStack(
                    Text("This is what your app will look like")
                        .Foreground(Theme.PrimaryText),
                    Button("Sample button"),
                    TextBox("").Placeholder("Sample input"),
                    ToggleSwitch(true),
                )
                .Background(Theme.SolidBackground)
                .Padding(24)
                .CornerRadius(8)
                .BorderBrush(Theme.CardStroke)
                .BorderThickness(1)
                .RequestedTheme(themePref switch           // ← PREVIEW THEME
                {
                    "light" => ElementTheme.Light,
                    "dark"  => ElementTheme.Dark,
                    _       => ElementTheme.Default,       // follows OS
                }),

            ).Padding(32).Gap(12)

        );
    }

    private Element NavItem(string label, string icon, bool selected) =>
        HStack(
            FontIcon(icon).FontSize(16).Foreground(
                selected ? Theme.Accent : Theme.SecondaryText),
            Text(label).Foreground(
                selected ? Theme.PrimaryText : Theme.SecondaryText)
        )
        .Background(selected ? Theme.SubtleFill : null)
        .Padding(10, 8)
        .CornerRadius(4)
        .Gap(10);
}


// ══════════════════════════════════════════════════════════════
// HeroLanding.cs — Dark hero + light content page
// ══════════════════════════════════════════════════════════════

public class HeroLandingPage : Component
{
    public override Element Render(RenderContext ctx)
    {
        return ScrollViewer(
            VStack(

                // ── Hero section: ALWAYS DARK ──
                // Dark dramatic header that doesn't change with OS theme.
                VStack(
                    Text("Welcome to Lime App")
                        .FontSize(42)
                        .FontWeight(FontWeights.Bold)
                        .Foreground(Theme.PrimaryText),      // → white (dark theme)

                    Text("The freshest way to build apps")
                        .FontSize(18)
                        .Foreground(Theme.SecondaryText),     // → light gray

                    HStack(
                        Button("Get Started")
                            .ApplyStyle("AccentButtonStyle"), // → lime green!
                        Button("Learn More"),                  // → dark theme button
                    ).Gap(12).Margin(0, 24, 0, 0)
                )
                .Background(Theme.Ref("BrandSubtle"))         // → very dark green tint
                .Padding(48, 64)
                .HorizontalAlignment(HorizontalAlignment.Stretch)
                .RequestedTheme(ElementTheme.Dark),            // ← ALWAYS DARK


                // ── Content below: follows OS theme ──
                VStack(
                    Text("Features")
                        .FontSize(28)
                        .Foreground(Theme.PrimaryText),       // adapts to OS

                    HStack(
                        FeatureCard("Fast",     "Built on WinUI3 for native speed"),
                        FeatureCard("Reactive", "React-inspired declarative model"),
                        FeatureCard("Themed",   "Adaptive themes that just work"),
                    ).Gap(16)
                )
                .Padding(48, 32)
                .RequestedTheme(ElementTheme.Default),         // ← follows OS

            )
        );
    }

    private Element FeatureCard(string title, string description) =>
        VStack(
            Text(title)
                .FontSize(18)
                .FontWeight(FontWeights.SemiBold)
                .Foreground(Theme.PrimaryText),
            Text(description)
                .Foreground(Theme.SecondaryText)
                .FontSize(13)
        )
        .Background(Theme.CardBackground)
        .BorderBrush(Theme.CardStroke)
        .BorderThickness(1)
        .CornerRadius(8)
        .Padding(20, 16)
        .Width(240);
}
```

**How surface theming works under the covers:**

1. `.RequestedTheme(ElementTheme.Dark)` sets `FrameworkElement.RequestedTheme` on the
   WinUI control. This is a single interop call that tells WinUI's resource system to
   resolve all `{ThemeResource}` lookups in this subtree as if the theme is Dark.

2. **WinUI built-in controls are free** — Button, Slider, TextBox, ToggleSwitch etc.
   all automatically use dark-mode styles within a `.RequestedTheme(Dark)` scope.
   No Reactor re-render needed; WinUI handles this natively at the framework level.

3. **Reactor Theme tokens also respect the scope** — When `Theme.PrimaryText` is used
   inside a `RequestedTheme(Dark)` scope, the reconciler's `GetEffectiveTheme(fe)`
   walks up the visual tree, finds the Dark override, and resolves from the Dark
   ThemeDictionary. So `Theme.PrimaryText` → white text inside the dark scope,
   dark text outside.

4. **Scopes nest and compose** — You can have:
   ```
   App (OS theme: Light)
     ├─ Hero section (Dark)
     │    └─ All controls dark-themed
     ├─ Content (Default → follows OS → Light)
     │    └─ All controls light-themed
     └─ Footer (Dark)
          └─ All controls dark-themed
   ```

5. **Theme.Default means "inherit from parent"** — Setting `.RequestedTheme(ElementTheme.Default)`
   explicitly resets a container to follow whatever its parent's theme is. Useful for
   "undoing" a forced theme in a nested section.

6. **Performance** — `RequestedTheme` is the most efficient theme mechanism because
   WinUI handles it entirely at the native layer. For WinUI built-in controls, there's
   zero Reactor overhead. Only Reactor components that use `Theme.*` tokens need a re-render
   when the effective theme changes for their scope.

---

## Property Precedence Analysis

```
WinUI Property Precedence (highest wins):
═════════════════════════════════════════
1. Animations
2. Local value          ← Tier 1: .Background("#red")
                        ← Tier 2: .Background(Theme.Accent) — resolved, set as local
3. TemplatedParent
4. Style setters        ← WinUI built-in {ThemeResource} lives here
5. Default style
6. Inheritance
7. Default value        ← Tier 3: property not set by Reactor

Tier 1 and Tier 2 both set LOCAL VALUES (precedence 2).
The difference is behavioral:
  - Tier 1: Set once, never changes (your explicit value)
  - Tier 2: Set at render time, RE-SET when theme changes (Reactor re-renders)

Tier 3 properties are never touched by Reactor, so WinUI's style system
has full control (ThemeResource bindings, visual states, etc.).
```

**Why Reactor re-render (not WinUI live binding) for Tier 2:**

| Approach | Pros | Cons |
|----------|------|------|
| **Reactor re-render** (chosen) | Simple, works for any property, custom tokens, subtree themes | Re-render on theme change (rare event, acceptable) |
| **WinUI resource injection** | No re-render needed | Requires per-control lightweight-styling key mapping, doesn't work for custom properties, complex ThemeDictionary management per control |
| **WinUI Binding** | Live update | Binding overhead per property, precedence 2 same as local, doesn't help with custom tokens |

Theme changes are rare (user-initiated, ~1-2 per app session), so a re-render is perfectly
acceptable. This is the same approach React, SwiftUI, and Compose all use.

---

## Performance Characteristics

| Operation | Cost | Frequency |
|-----------|------|-----------|
| `.Background("#red")` (Tier 1) | 1 interop call | Per render |
| `.Background(Theme.Accent)` (Tier 2) | 1 resource lookup + 1 interop call | Per render |
| Theme change re-render (Tier 2) | Re-render all subscribed components | ~Rare (user-initiated) |
| Unset property (Tier 3) | Zero cost | Always |
| `UseTheme()` hook | 1 weak-ref subscription | Per component mount |
| `.RequestedTheme(Dark)` | 1 interop call | Per render |
| `ThemeScope` resource injection | N resources × 2 themes | Per mount |
| `ReactorApp.DefineThemeResources()` | N resources × 2 themes | Once at startup |

**Optimization: ThemeRef resolution caching**

```csharp
// Cache resolved theme resources per theme to avoid repeated lookups:
internal static class ThemeCache
{
    private static ElementTheme _cachedTheme;
    private static readonly Dictionary<string, object> _cache = new();

    internal static T Resolve<T>(string key, ElementTheme theme) where T : class
    {
        if (theme != _cachedTheme)
        {
            _cache.Clear();
            _cachedTheme = theme;
        }

        if (_cache.TryGetValue(key, out var cached))
            return (T)cached;

        var value = ResolveFromAppResources<T>(key, theme);
        if (value is not null) _cache[key] = value;
        return value!;
    }
}
```

---

## Visual State Manager Interaction (Deep Analysis)

### How WinUI Visual States Actually Work

WinUI3 control templates implement visual states (PointerOver, Pressed, Disabled) using
**Storyboard animations**, not VisualState.Setters. Here's a simplified Button template:

```xml
<ControlTemplate TargetType="Button">
  <ContentPresenter x:Name="ContentPresenter"
      Background="{TemplateBinding Background}"
      BorderBrush="{TemplateBinding BorderBrush}"
      Foreground="{TemplateBinding Foreground}">
    <VisualStateManager.VisualStateGroups>
      <VisualStateGroup x:Name="CommonStates">
        <VisualState x:Name="Normal"/>
        <VisualState x:Name="PointerOver">
          <Storyboard>
            <ObjectAnimationUsingKeyFrames
                Storyboard.TargetName="ContentPresenter"
                Storyboard.TargetProperty="Background">
              <DiscreteObjectKeyFrame KeyTime="0"
                  Value="{ThemeResource ButtonBackgroundPointerOver}"/>
            </ObjectAnimationUsingKeyFrames>
            <!-- Same for BorderBrush, Foreground -->
          </Storyboard>
        </VisualState>
        <VisualState x:Name="Pressed">
          <Storyboard>
            <ObjectAnimationUsingKeyFrames ...
                Value="{ThemeResource ButtonBackgroundPressed}"/>
          </Storyboard>
        </VisualState>
        <VisualState x:Name="Disabled">
          <Storyboard>
            <ObjectAnimationUsingKeyFrames ...
                Value="{ThemeResource ButtonBackgroundDisabled}"/>
          </Storyboard>
        </VisualState>
      </VisualStateGroup>
    </VisualStateManager.VisualStateGroups>
  </ContentPresenter>
</ControlTemplate>
```

Key observations:
- **Normal state** has no storyboard — the control just uses its property values as-is
- **PointerOver/Pressed/Disabled** each animate properties to per-state ThemeResource keys
- Each property × state has its own resource key: `ButtonBackground`, `ButtonBackgroundPointerOver`,
  `ButtonBackgroundPressed`, `ButtonBackgroundDisabled`
- The animations target the ContentPresenter *inside* the template, not the Button itself

### WinUI3's Precedence — Different from WPF

WinUI3 follows **Silverlight-era semantics**, not WPF semantics. The critical difference
is in how `GetEffectiveValue()` resolves conflicts between local values and animations.

From the WinUI source (`ModifiedValue.cpp`):

```
 Precedence (highest to lowest):
 ┌─────────────────────────────────────────────────────┐
 │ 1. Active animation                                  │
 │    ...BUT with the fvsLocalValueNewerThanAnimatedValue│
 │    flag: if a local value was set AFTER the animation │
 │    started, the local value wins.                     │
 ├─────────────────────────────────────────────────────┤
 │ 2. Local value  (button.Background = redBrush)       │
 ├─────────────────────────────────────────────────────┤
 │ 3. TemplatedParent (TemplateBinding)                 │
 ├─────────────────────────────────────────────────────┤
 │ 4. Style setters (implicit/explicit)                 │
 │    incl. {ThemeResource} bindings in style setters   │
 ├─────────────────────────────────────────────────────┤
 │ 5. Default style (theme style)                       │
 ├─────────────────────────────────────────────────────┤
 │ 6. Inheritance                                       │
 ├─────────────────────────────────────────────────────┤
 │ 7. Default value                                     │
 └─────────────────────────────────────────────────────┘
```

The engine uses a `FullValueSource` bitfield with a critical flag:

```cpp
// From ModifiedValue.h
enum FullValueSource {
    fvsIsAnimated                       = 0x0010,
    fvsLocalValueNewerThanAnimatedValue = 0x1000  // ← THE KEY FLAG
};
```

And the resolution logic (from `ModifiedValue.cpp` GetEffectiveValue):

```cpp
if (IsAnimated()) {
    if (m_fullValueSource & fvsLocalValueNewerThanAnimatedValue) {
        // LOCAL VALUE WINS — even over active animation
        // Comment from WinUI source: "This is different from WPF and is done
        // because some legacy SL apps depend on this and because SL Animation
        // thinks that it is better design for an animation in filling period
        // to be trumped by a local value."
        IFC_RETURN(GetBaseValue(pValue));
    } else {
        IFC_RETURN(GetAnimatedValue(pValue));
    }
}
```

### The Problem: What Happens When Reactor Sets a Local Value

**Scenario:** Reactor sets `button.Background = redBrush` (a local value), then the
user hovers over the button.

**Timeline:**

```
Step 1: Reactor reconciler sets  button.Background = redBrush
        → WinUI sets BaseValueSource = Local
        → WinUI sets fvsLocalValueNewerThanAnimatedValue = true
        
Step 2: User hovers → VSM transitions to "PointerOver" state
        → Storyboard starts ObjectAnimationUsingKeyFrames
        → SetAnimatedValue() is called with ButtonBackgroundPointerOver brush
        → fvsIsAnimated = true
        → fvsLocalValueNewerThanAnimatedValue is CLEARED by SetAnimatedValue()
        
Step 3: GetEffectiveValue() resolves:
        → IsAnimated? YES
        → LocalValueNewerThanAnimatedValue? NO (was cleared in Step 2)
        → Returns ANIMATED value (ButtonBackgroundPointerOver) ← animation wins!
        
Step 4: User moves mouse away → VSM transitions back to "Normal" state
        → Storyboard stops, ClearAnimatedValue() is called
        → fvsIsAnimated = false
        → GetEffectiveValue() returns base value = redBrush ← local value returns
```

**So animations DO override local values during the active state.** But there's a
critical subtlety: the animation targets the **ContentPresenter inside the template**,
not the Button control itself. The relationship works like this:

```
Button.Background = redBrush                    ← LOCAL VALUE (Reactor sets this)
    └── ContentPresenter.Background
          = {TemplateBinding Background}         ← REFLECTS Button.Background
          
VisualState "PointerOver" animation targets:
    ContentPresenter.Background                  ← ANIMATED VALUE on ContentPresenter
```

**What actually happens:**

1. Button.Background is set to redBrush (local value on the Button)
2. ContentPresenter.Background picks it up via TemplateBinding → shows red
3. User hovers → Storyboard animates ContentPresenter.Background directly
4. The animation sets ContentPresenter.Background = ButtonBackgroundPointerOver brush
5. **The animation targets ContentPresenter, bypassing the TemplateBinding**
6. ContentPresenter shows the hover color ✓
7. User leaves → animation stops → TemplateBinding re-asserts → back to red ✓

**Result: Visual states DO work for standard controls even with Reactor's local value!**

The TemplateBinding on ContentPresenter is effectively "disconnected" while the
storyboard animation is active, and reconnects when it stops.

### Where It Breaks: Non-TemplateBinding Scenarios

The above works for Button because its template uses TemplateBinding. But some controls
have different patterns:

**Case 1: Controls where properties are set on the root template element**
If the control template sets properties directly on the root element (not via
TemplateBinding on an inner element), then Reactor's local value and the VSM animation
target the SAME element. In this case:

```
Reactor sets: control.SomeProperty = value        ← LOCAL on control
VSM sets:  control.SomeProperty via animation  ← ANIMATED on same element
```

Due to the Silverlight-era flag logic:
- If animation starts AFTER Reactor set the value → `SetAnimatedValue()` clears the
  "newer" flag → animation wins during active state ✓
- If Reactor re-renders WHILE animation is active → sets new local value → sets
  "newer" flag again → **local value wins, animation is overridden** ✗

**Case 2: Lightweight-styled controls**
Controls that use per-control `Resources` for lightweight styling:

```xml
<ContentPresenter Background="{ThemeResource ButtonBackground}">
```

Here the ContentPresenter doesn't use TemplateBinding at all — it reads directly
from ThemeResource. The Storyboard animation targets ContentPresenter.Background
directly, overriding the ThemeResource during active states. This works correctly
unless Reactor sets ContentPresenter.Background as a local value (which it can't,
since it only has access to the Button, not the internal ContentPresenter).

### The Lightweight Styling Opportunity

WinUI's **lightweight styling** mechanism provides the cleanest integration path.
Instead of setting a property directly, you inject a resource into the control's
own resource dictionary:

```csharp
// Instead of:
button.Background = redBrush;  // LOCAL VALUE — overrides template binding

// Do this:
button.Resources["ButtonBackground"] = redBrush;  // RESOURCE OVERRIDE
```

**How this flows through the system:**

```
Button.Resources["ButtonBackground"] = redBrush
                    ↓
Template's ContentPresenter:
    Background="{ThemeResource ButtonBackground}"
    → Resolves UP the resource tree
    → Finds "ButtonBackground" in Button.Resources (nearest scope)
    → Uses redBrush ✓

VisualState "PointerOver" animation:
    ContentPresenter.Background = {ThemeResource ButtonBackgroundPointerOver}
    → "ButtonBackgroundPointerOver" is a DIFFERENT key
    → Resolves to system default (or your override if you set that too)
    → Hover state still shows correct hover color ✓
```

**Advantages:**
- The "rest" state uses your custom color
- PointerOver/Pressed/Disabled states still work with their own resource keys
- No local value is set, so no precedence conflict
- You can selectively override individual states by setting their keys too

**Per-control resource key mapping (from WinUI source):**

```
Button:     Background → "ButtonBackground"
            Foreground → "ButtonForeground"
            BorderBrush → "ButtonBorderBrush"
            (+ PointerOver, Pressed, Disabled variants for each)

TextBox:    Background → "TextControlBackground"
            Foreground → "TextControlForeground"
            BorderBrush → "TextControlBorderBrush"
            (+ Focused, PointerOver, Disabled variants)

ComboBox:   Background → "ComboBoxBackground"
            ...

ToggleButton: Background → "ToggleButtonBackground"
              ...
```

Each control type has its own prefix convention. The full mapping for WinUI's
standard controls is defined in `*_themeresources.xaml` files in the WinUI source.

### Design Decision: Three Strategies for Reactor

Given this analysis, Reactor should support three strategies, chosen by the developer:

#### Strategy 1: Direct Local Value (today's default)

```csharp
Button("Click").Background("#FF5733")
```

- Sets `button.Background = brush` (local value)
- **Visual states:** Partially work — PointerOver/Pressed animations on the
  ContentPresenter will still fire (they target a different element via the template).
  But the "rest" state is locked to your color. If Reactor re-renders during an active
  animation, the re-render will re-set the local value.
- **Use when:** You want an exact color and don't care about hover/pressed effects
  being themed, OR the control's template uses TemplateBinding (most standard controls).

#### Strategy 2: Lightweight Styling Injection (NEW — preserves visual states)

```csharp
Button("Click").Background(Theme.Accent, preserveStates: true)
// or a dedicated API:
Button("Click").ThemedBackground(Theme.Accent)
```

- Sets `button.Resources["ButtonBackground"] = resolvedBrush`
- Also optionally generates state variants:
  ```csharp
  button.Resources["ButtonBackgroundPointerOver"] = lighterBrush;
  button.Resources["ButtonBackgroundPressed"] = darkerBrush;
  ```
- **Visual states:** Fully preserved — each state resolves its own resource key
- **Use when:** You want theme-aware colors that play nicely with the full visual
  state system (hover, pressed, disabled all get appropriate variants)
- **Requires:** Per-control-type resource key mapping table

#### Strategy 3: Full State Override (NEW — explicit per-state control)

```csharp
Button("Click")
    .Background(Theme.Accent)
    .BackgroundPointerOver(Theme.AccentLight)
    .BackgroundPressed(Theme.AccentDark)
    .BackgroundDisabled(Theme.SurfaceDisabled)
```

- Sets lightweight styling keys for each state explicitly
- **Visual states:** Fully controlled — you define the exact color for each state
- **Use when:** You need pixel-perfect control over every interaction state

### Implementation: Lightweight Styling in the Reconciler

The reconciler changes required for Strategy 2 (lightweight styling):

```csharp
internal void ApplyModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
{
    // For each themed property with preserveStates:
    if (m.BackgroundThemeRef != null && m.PreserveBackgroundStates)
    {
        var resolved = ResolveThemeRef(fe, m.BackgroundThemeRef);
        var controlType = fe.GetType();
        
        if (LightweightStyleMap.TryGetKeys(controlType, "Background", out var keys))
        {
            // Set the rest-state resource
            fe.Resources[keys.Rest] = resolved;
            
            // Optionally generate state variants
            if (m.BackgroundPointerOverThemeRef != null)
                fe.Resources[keys.PointerOver] = ResolveThemeRef(fe, m.BackgroundPointerOverThemeRef);
            if (m.BackgroundPressedThemeRef != null)
                fe.Resources[keys.Pressed] = ResolveThemeRef(fe, m.BackgroundPressedThemeRef);
            if (m.BackgroundDisabledThemeRef != null)
                fe.Resources[keys.Disabled] = ResolveThemeRef(fe, m.BackgroundDisabledThemeRef);
        }
        else
        {
            // Fallback: control type not in mapping, use direct local value
            fe.Background = resolved;
        }
    }
    else if (m.Background != null && m.Background != oldM?.Background)
    {
        // Today's behavior: direct local value
        fe.Background = m.Background;
    }
}
```

The `LightweightStyleMap` would be a static dictionary:

```csharp
internal static class LightweightStyleMap
{
    // Maps (ControlType, PropertyName) → resource key names
    private static readonly Dictionary<(Type, string), StateKeys> _map = new()
    {
        [(typeof(Button), "Background")] = new("ButtonBackground",
            "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
            "ButtonBackgroundDisabled"),
        [(typeof(Button), "Foreground")] = new("ButtonForeground",
            "ButtonForegroundPointerOver", "ButtonForegroundPressed",
            "ButtonForegroundDisabled"),
        [(typeof(Button), "BorderBrush")] = new("ButtonBorderBrush",
            "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "ButtonBorderBrushDisabled"),
        [(typeof(TextBox), "Background")] = new("TextControlBackground",
            "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBackgroundDisabled"),
        // ... etc for each control type
    };

    internal record StateKeys(
        string Rest, string PointerOver, string Pressed, string Disabled);

    internal static bool TryGetKeys(Type controlType, string property,
        out StateKeys keys)
        => _map.TryGetValue((controlType, property), out keys!);
}
```

### Also Discovered: Reactor Doesn't ClearValue on Null Transitions

A secondary issue found during this analysis: when a Reactor element's modifier goes
from non-null to null between renders (e.g., a component conditionally applies
`.Background()` in one render but not the next), the reconciler does NOT call
`ClearValue()`. The WinUI control retains the stale local value.

```csharp
// Render 1: Background = red
if (m.Background.HasValue && m.Background != oldM?.Background)
    fe.Background = m.Background.Value;  // ← Sets local value

// Render 2: Background = null (user removed .Background() call)
// m.Background.HasValue is FALSE → condition not entered → nothing happens
// fe.Background is STILL red from Render 1!
```

This should be fixed independently of theming:

```csharp
if (m.Background.HasValue && m.Background != oldM?.Background)
    fe.Background = m.Background.Value;
else if (!m.Background.HasValue && oldM?.Background.HasValue == true)
    fe.ClearValue(Control.BackgroundProperty);  // ← Reset to style/theme value
```

This fix would also improve theming: clearing a local value allows the WinUI
style system (ThemeResource bindings, visual states) to reassert control.

---

## Migration / Backward Compatibility

**Zero breaking changes.** The design is purely additive:

| Existing Code | Behavior |
|---------------|----------|
| `.Background("#red")` | Unchanged — local value |
| `.Background(myBrush)` | Unchanged — local value |
| `.ApplyStyle("StyleName")` | Unchanged — WinUI Style |
| No `.Background()` call | Unchanged — WinUI default |
| `ThemeResource.Brush("key")` | Unchanged — static helper still works |

**New capabilities are opt-in:**

| New Code | Behavior |
|----------|----------|
| `.Background(Theme.Accent)` | NEW — theme-reactive |
| `.RequestedTheme(Dark)` | NEW — subtree theme |
| `Theme.Ref("custom")` | NEW — custom resource ref |
| `UseTheme()` | NEW — theme-aware component |
| `ThemeScope(...)` | NEW — scoped resources |

---

## Implementation Phases

### Phase 1 (Foundation)
- [ ] ThemeRef record struct
- [ ] Theme static class with built-in tokens
- [ ] Modifier overloads for Background, Foreground, BorderBrush
- [ ] ThemeBindings property on Element
- [ ] Reconciler: resolve ThemeRef → set local value

### Phase 2 (Theme Reactivity)
- [ ] ThemeMonitor class (subscribe to ActualThemeChanged)
- [ ] UseTheme() hook on RenderContext
- [ ] Auto-subscribe components with ThemeBindings to theme changes
- [ ] ThemeCache for efficient re-resolution

### Phase 3 (Subtree Theming)
- [ ] RequestedTheme modifier + reconciler support
- [ ] ThemeRef resolution respects subtree RequestedTheme
- [ ] GetEffectiveTheme() tree walk

### Phase 4 (Custom Resources)
- [ ] ReactorThemeResources class
- [ ] ReactorApp.DefineThemeResources() — inject into Application.Resources
- [ ] ThemeScope component for scoped resource injection

### Phase 5 (Polish & Advanced)
- [ ] Lightweight-styling resource injection mode (preserveStates)
- [ ] Per-control resource key mapping table
- [ ] HighContrast support
- [ ] Design token documentation
- [ ] Performance profiling and optimization

---

## Open Questions for Discussion

1. **ThemeRef syntax for non-brush properties?**
   Currently only Brush properties have theme tokens. Should we extend to
   CornerRadius, Thickness, Double? e.g., `.CornerRadius(Theme.ControlCornerRadius)`

2. **Theme change animation?**
   Should Reactor support animated transitions between themes
   (fade, cross-dissolve) or just snap to the new values?

3. **Style bundles?**
   Reusable modifier groups that include both concrete and theme values:
   ```csharp
   var cardStyle = StyleBundle.Create(el => el
       .Background(Theme.Ref("CardSurface"))
       .BorderBrush(Theme.Ref("CardBorder"))
       .CornerRadius(8)
       .Padding(16));

   VStack(content).Apply(cardStyle);
   ```

4. **XAML style interop?**
   Should Reactor be able to load `.xaml` resource dictionaries for theme
   definitions, enabling design-tool workflows?
