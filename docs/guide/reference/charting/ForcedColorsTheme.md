# ForcedColorsTheme

Holds the actual system high-contrast colors queried from [`UISettings.GetColorValue`](https://learn.microsoft.com/uwp/api/windows.ui.viewmanagement.uisettings.getcolorvalue). The host populates this each render cycle so chart brushes adapt to the user's chosen high-contrast theme (including custom themes).

```csharp
public sealed class ForcedColorsTheme
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](../../charting.md)

## Constructors

### `ForcedColorsTheme`

```csharp
public ForcedColorsTheme()
```

## Properties

### `Background`

System background (Canvas / Window).

```csharp
public Color Background  // get, init
```

### `Foreground`

System foreground (CanvasText / WindowText).

```csharp
public Color Foreground  // get, init
```

### `GrayText`

Disabled / secondary text (GrayText).

```csharp
public Color GrayText  // get, init
```

### `Highlight`

System highlight / accent.

```csharp
public Color Highlight  // get, init
```

### `HighlightText`

Text on highlighted background.

```csharp
public Color HighlightText  // get, init
```

### `Hotlight`

Hyperlink text color.

```csharp
public Color Hotlight  // get, init
```

### `SeriesColors`

Distinct colors for chart series, derived from system HC colors. Cycles through Foreground, Highlight, Hotlight, GrayText.

```csharp
public Color[] SeriesColors  // get, init
```

## Methods

### `FromSystem`

Queries the current system high-contrast colors from [`UISettings`](https://learn.microsoft.com/uwp/api/windows.ui.viewmanagement.uisettings). Returns [`ForcedColorsTheme.Default`](#default) if the query fails (headless environment).

```csharp
public static ForcedColorsTheme FromSystem()
```

## Fields

### `Default`

Safe fallback when system colors cannot be queried (headless / unit tests). Uses standard HC Black theme colors.

```csharp
public static readonly ForcedColorsTheme Default
```
