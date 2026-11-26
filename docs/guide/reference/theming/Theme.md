# Theme

Provides semantic theme tokens and custom resource references. All tokens resolve from WinUI's resource system and automatically adapt when the theme changes (Light ↔ Dark).

Usage: `Button("Go").Background(Theme.Accent)`

```csharp
public static class Theme
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Theming Tokens](../../theming-tokens.md)

## Properties

### `Accent`

```csharp
public static ThemeRef Accent  // get
```

### `AccentDisabled`

```csharp
public static ThemeRef AccentDisabled  // get
```

### `AccentSecondary`

```csharp
public static ThemeRef AccentSecondary  // get
```

### `AccentTertiary`

```csharp
public static ThemeRef AccentTertiary  // get
```

### `AccentText`

```csharp
public static ThemeRef AccentText  // get
```

### `CardBackground`

```csharp
public static ThemeRef CardBackground  // get
```

### `CardStroke`

```csharp
public static ThemeRef CardStroke  // get
```

### `ControlFill`

```csharp
public static ThemeRef ControlFill  // get
```

### `ControlFillDisabled`

```csharp
public static ThemeRef ControlFillDisabled  // get
```

### `ControlFillInputActive`

```csharp
public static ThemeRef ControlFillInputActive  // get
```

### `ControlFillSecondary`

```csharp
public static ThemeRef ControlFillSecondary  // get
```

### `ControlFillTertiary`

```csharp
public static ThemeRef ControlFillTertiary  // get
```

### `ControlStroke`

```csharp
public static ThemeRef ControlStroke  // get
```

### `ControlStrokeSecondary`

```csharp
public static ThemeRef ControlStrokeSecondary  // get
```

### `DisabledText`

```csharp
public static ThemeRef DisabledText  // get
```

### `DividerStroke`

```csharp
public static ThemeRef DividerStroke  // get
```

### `LayerFill`

```csharp
public static ThemeRef LayerFill  // get
```

### `PrimaryText`

```csharp
public static ThemeRef PrimaryText  // get
```

### `SecondaryText`

```csharp
public static ThemeRef SecondaryText  // get
```

### `SmokeFill`

```csharp
public static ThemeRef SmokeFill  // get
```

### `SolidBackground`

```csharp
public static ThemeRef SolidBackground  // get
```

### `SubtleFill`

```csharp
public static ThemeRef SubtleFill  // get
```

### `SurfaceStroke`

```csharp
public static ThemeRef SurfaceStroke  // get
```

### `SystemAttention`

```csharp
public static ThemeRef SystemAttention  // get
```

### `SystemAttentionBackground`

```csharp
public static ThemeRef SystemAttentionBackground  // get
```

### `SystemCaution`

```csharp
public static ThemeRef SystemCaution  // get
```

### `SystemCautionBackground`

```csharp
public static ThemeRef SystemCautionBackground  // get
```

### `SystemCritical`

```csharp
public static ThemeRef SystemCritical  // get
```

### `SystemCriticalBackground`

```csharp
public static ThemeRef SystemCriticalBackground  // get
```

### `SystemNeutral`

```csharp
public static ThemeRef SystemNeutral  // get
```

### `SystemNeutralBackground`

```csharp
public static ThemeRef SystemNeutralBackground  // get
```

### `SystemSolidAttention`

```csharp
public static ThemeRef SystemSolidAttention  // get
```

### `SystemSolidNeutral`

```csharp
public static ThemeRef SystemSolidNeutral  // get
```

### `SystemSuccess`

```csharp
public static ThemeRef SystemSuccess  // get
```

### `SystemSuccessBackground`

```csharp
public static ThemeRef SystemSuccessBackground  // get
```

### `TertiaryText`

```csharp
public static ThemeRef TertiaryText  // get
```

## Methods

### `NotifyResourcesChanged`

Tells Reactor that application resources were edited at runtime, so theme references resolve again and every live host re-renders with the new values.

```csharp
public static void NotifyResourcesChanged()
```

**Remarks**

Call it after changing `Application.Current.Resources` in a way that is *not* a theme change — replacing a brush in a `ThemeDictionaries` entry, merging or removing a brand dictionary, or an inspector's live resource edit. Reactor caches each resolved `(resource key, theme)` brush and only drops that cache when the effective theme or the system palette changes, so without this call such an edit is not seen. Not needed for a Light/Dark/high-contrast switch (`RequestedTheme`) or for mutating an existing brush's `Color`; both are already picked up.

What it does: clears the resolution cache behind [`ThemeRef`](ThemeRef.md), then asks every live [`ReactorHost`](../hosting/ReactorHost.md) and [`ReactorHostControl`](../hosting/ReactorHostControl.md) for a full re-render that bypasses component memoization and the reconciler's skip of unchanged elements, so memoized and reused subtrees are refreshed too. That re-resolves `.Resources(...)` theme overrides, re-applies [`ThemeRef`](ThemeRef.md) modifiers such as `.Background(Theme.Accent)`, and re-runs any [`ThemeRef.Resolve`](ThemeRef.md#resolve) call in a `Render` method.

Callable from any thread; the re-renders are scheduled on each host's UI thread. Await a host's `WaitForIdleAsync()` to observe the result.

On a host's UI thread, a host that has no content yet re-renders inline, and its render can throw. Every other host is still notified; then the exception is rethrown (an [`AggregateException`](https://learn.microsoft.com/dotnet/api/system.aggregateexception) when several hosts threw).

### `Ref`

Reference any WinUI theme resource by key name. The resource must exist in the WinUI resource tree (e.g., defined in XamlControlsResources or added via app resources).

```csharp
public static ThemeRef Ref(string resourceKey)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `resourceKey` | `string` |  |
