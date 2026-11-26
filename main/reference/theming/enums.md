---
title: "Enums"
description: "The enumerations in this category."
---

**Learn more:** [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## `BackdropKind` [#backdropkind]

Declarative selector for the WinUI [`SystemBackdrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop) applied to a Reactor-hosted [`Window`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window). Set via the [`Backdrop`](/microsoft-ui-reactor/main/reference/modifiers/appearance#backdrop) modifier on a Reactor root tree.

```csharp
public enum BackdropKind
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks [#remarks]

Spec 033 §6. Mica/Acrylic backdrops are a Windows 11 (build 22000+) feature. On Windows 10 the WinUI [`Window.SystemBackdrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window.systembackdrop) setter no-ops; we delegate to that behavior rather than reimplementing the fallback ladder.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` | No backdrop. Clears any previously-set backdrop on the host window. |
| `Mica` | `1` | Mica (window-tint material). Best for primary application surfaces on Win11. |
| `MicaAlt` | `2` | Mica (alt). The same material with a slightly different luminance/tint balance. |
| `DesktopAcrylic` | `3` | Desktop Acrylic (translucent blur). Best for transient surfaces — flyouts, dialogs. |
| `AcrylicThin` | `4` | Acrylic (thin variant). Subtler blur than [`BackdropKind.DesktopAcrylic`](#values). |
| `Transparent` | `5` | Transparent backdrop. Requires Windows App SDK support; unsupported platforms fall back to no backdrop. |

## `ColorScheme` [#colorscheme]

Represents a color scheme variant a component can adapt its rendering to.

Note that [`UseColorScheme`](/microsoft-ui-reactor/main/reference/hooks/UseColorScheme) reports the app-global theme and only ever returns [`ColorScheme.Light`](#values_1) or [`ColorScheme.Dark`](#values_1); [`ColorScheme.HighContrast`](#values_1) is produced solely by the [`ElementTheme.Default`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.elementtheme.default) mapping arm below. Use [`UseHighContrast`](/microsoft-ui-reactor/main/reference/hooks/UseHighContrast) to detect forced-colors mode.

```csharp
public enum ColorScheme
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Light` | `0` | The standard light theme. |
| `Dark` | `1` | The standard dark theme. |
| `HighContrast` | `2` | Windows High Contrast mode is active. |
