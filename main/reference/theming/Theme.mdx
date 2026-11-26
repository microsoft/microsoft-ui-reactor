---
title: "Theme"
description: "Provides semantic theme tokens and custom resource references. All tokens resolve from WinUI's resource system and automatically adapt when the theme changes (Light ↔ Dark)."
---

Usage: `Button("Go").Background(Theme.Accent)`

```csharp
public static class Theme
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Theming Tokens](/microsoft-ui-reactor/main/guides/theming-tokens)

## Properties [#properties]

### `Accent` [#accent]

```csharp
public static ThemeRef Accent  // get
```

### `AccentDisabled` [#accentdisabled]

```csharp
public static ThemeRef AccentDisabled  // get
```

### `AccentSecondary` [#accentsecondary]

```csharp
public static ThemeRef AccentSecondary  // get
```

### `AccentTertiary` [#accenttertiary]

```csharp
public static ThemeRef AccentTertiary  // get
```

### `AccentText` [#accenttext]

```csharp
public static ThemeRef AccentText  // get
```

### `CardBackground` [#cardbackground]

```csharp
public static ThemeRef CardBackground  // get
```

### `CardStroke` [#cardstroke]

```csharp
public static ThemeRef CardStroke  // get
```

### `ControlFill` [#controlfill]

```csharp
public static ThemeRef ControlFill  // get
```

### `ControlFillDisabled` [#controlfilldisabled]

```csharp
public static ThemeRef ControlFillDisabled  // get
```

### `ControlFillInputActive` [#controlfillinputactive]

```csharp
public static ThemeRef ControlFillInputActive  // get
```

### `ControlFillSecondary` [#controlfillsecondary]

```csharp
public static ThemeRef ControlFillSecondary  // get
```

### `ControlFillTertiary` [#controlfilltertiary]

```csharp
public static ThemeRef ControlFillTertiary  // get
```

### `ControlStroke` [#controlstroke]

```csharp
public static ThemeRef ControlStroke  // get
```

### `ControlStrokeSecondary` [#controlstrokesecondary]

```csharp
public static ThemeRef ControlStrokeSecondary  // get
```

### `DisabledText` [#disabledtext]

```csharp
public static ThemeRef DisabledText  // get
```

### `DividerStroke` [#dividerstroke]

```csharp
public static ThemeRef DividerStroke  // get
```

### `LayerFill` [#layerfill]

```csharp
public static ThemeRef LayerFill  // get
```

### `PrimaryText` [#primarytext]

```csharp
public static ThemeRef PrimaryText  // get
```

### `SecondaryText` [#secondarytext]

```csharp
public static ThemeRef SecondaryText  // get
```

### `SmokeFill` [#smokefill]

```csharp
public static ThemeRef SmokeFill  // get
```

### `SolidBackground` [#solidbackground]

```csharp
public static ThemeRef SolidBackground  // get
```

### `SubtleFill` [#subtlefill]

```csharp
public static ThemeRef SubtleFill  // get
```

### `SurfaceStroke` [#surfacestroke]

```csharp
public static ThemeRef SurfaceStroke  // get
```

### `SystemAttention` [#systemattention]

```csharp
public static ThemeRef SystemAttention  // get
```

### `SystemAttentionBackground` [#systemattentionbackground]

```csharp
public static ThemeRef SystemAttentionBackground  // get
```

### `SystemCaution` [#systemcaution]

```csharp
public static ThemeRef SystemCaution  // get
```

### `SystemCautionBackground` [#systemcautionbackground]

```csharp
public static ThemeRef SystemCautionBackground  // get
```

### `SystemCritical` [#systemcritical]

```csharp
public static ThemeRef SystemCritical  // get
```

### `SystemCriticalBackground` [#systemcriticalbackground]

```csharp
public static ThemeRef SystemCriticalBackground  // get
```

### `SystemNeutral` [#systemneutral]

```csharp
public static ThemeRef SystemNeutral  // get
```

### `SystemNeutralBackground` [#systemneutralbackground]

```csharp
public static ThemeRef SystemNeutralBackground  // get
```

### `SystemSolidAttention` [#systemsolidattention]

```csharp
public static ThemeRef SystemSolidAttention  // get
```

### `SystemSolidNeutral` [#systemsolidneutral]

```csharp
public static ThemeRef SystemSolidNeutral  // get
```

### `SystemSuccess` [#systemsuccess]

```csharp
public static ThemeRef SystemSuccess  // get
```

### `SystemSuccessBackground` [#systemsuccessbackground]

```csharp
public static ThemeRef SystemSuccessBackground  // get
```

### `TertiaryText` [#tertiarytext]

```csharp
public static ThemeRef TertiaryText  // get
```

## Methods [#methods]

### `NotifyResourcesChanged` [#notifyresourceschanged]

Tells Reactor that application resources were edited at runtime, so theme references resolve again and every live host re-renders with the new values.

```csharp
public static void NotifyResourcesChanged()
```

**Remarks**

Call it after changing `Application.Current.Resources` in a way that is *not* a theme change — replacing a brush in a `ThemeDictionaries` entry, merging or removing a brand dictionary, or an inspector's live resource edit. Reactor caches each resolved `(resource key, theme)` brush and only drops that cache when the effective theme or the system palette changes, so without this call such an edit is not seen. Not needed for a Light/Dark/high-contrast switch (`RequestedTheme`) or for mutating an existing brush's `Color`; both are already picked up.

What it does: clears the resolution cache behind [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef), then asks every live [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) and [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl) for a full re-render that bypasses component memoization and the reconciler's skip of unchanged elements, so memoized and reused subtrees are refreshed too. That re-resolves `.Resources(...)` theme overrides, re-applies [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef) modifiers such as `.Background(Theme.Accent)`, and re-runs any [`ThemeRef.Resolve`](/microsoft-ui-reactor/main/reference/theming/ThemeRef#resolve) call in a `Render` method.

Callable from any thread; the re-renders are scheduled on each host's UI thread. Await a host's `WaitForIdleAsync()` to observe the result.

On a host's UI thread, a host that has no content yet re-renders inline, and its render can throw. Every other host is still notified; then the exception is rethrown (an [`AggregateException`](https://learn.microsoft.com/dotnet/api/system.aggregateexception) when several hosts threw).

### `Ref` [#ref]

Reference any WinUI theme resource by key name. The resource must exist in the WinUI resource tree (e.g., defined in XamlControlsResources or added via app resources).

```csharp
public static ThemeRef Ref(string resourceKey)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `resourceKey` | `string` |  |
