---
title: "Icon data"
description: "Records that describe an icon."
---

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls)

## `BitmapIconData` [#bitmapicondata]

```csharp
public record BitmapIconData(Uri Source, bool ShowAsMonochrome = true) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors [#constructors]

#### `BitmapIconData` [#bitmapicondata_1]

```csharp
public BitmapIconData(Uri Source, bool ShowAsMonochrome = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Source` | `Uri` |  |  |
| `ShowAsMonochrome` | `bool` | `true` |  |

### Properties [#properties]

#### `ShowAsMonochrome` [#showasmonochrome]

```csharp
public bool ShowAsMonochrome  // get, init
```

#### `Source` [#source]

```csharp
public Uri Source  // get, init
```

## `FontIconData` [#fonticondata]

```csharp
public record FontIconData(string Glyph, string? FontFamily = null, double? FontSize = null) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors [#constructors_1]

#### `FontIconData` [#fonticondata_1]

```csharp
public FontIconData(string Glyph, string? FontFamily = null, double? FontSize = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Glyph` | `string` |  |  |
| `FontFamily` | `string?` | `null` |  |
| `FontSize` | `double?` | `null` |  |

### Properties [#properties_1]

#### `FontFamily` [#fontfamily]

```csharp
public string? FontFamily  // get, init
```

#### `FontSize` [#fontsize]

```csharp
public double? FontSize  // get, init
```

#### `Glyph` [#glyph]

```csharp
public string Glyph  // get, init
```

## `IconData` [#icondata]

```csharp
public abstract record IconData
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_2]

#### `IconData` [#icondata_1]

```csharp
protected IconData()
```

## `ImageIconData` [#imageicondata]

```csharp
public record ImageIconData(Uri Source) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors [#constructors_3]

#### `ImageIconData` [#imageicondata_1]

```csharp
public ImageIconData(Uri Source)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Source` | `Uri` |  |

### Properties [#properties_2]

#### `Source` [#source_1]

```csharp
public Uri Source  // get, init
```

## `PathIconData` [#pathicondata]

```csharp
public record PathIconData(string Data) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors [#constructors_4]

#### `PathIconData` [#pathicondata_1]

```csharp
public PathIconData(string Data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Data` | `string` |  |

### Properties [#properties_3]

#### `Data` [#data]

```csharp
public string Data  // get, init
```

## `SymbolIconData` [#symbolicondata]

```csharp
public record SymbolIconData(string Symbol) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors [#constructors_5]

#### `SymbolIconData` [#symbolicondata_1]

```csharp
public SymbolIconData(string Symbol)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Symbol` | `string` |  |

### Properties [#properties_4]

#### `Symbol` [#symbol]

```csharp
public string Symbol  // get, init
```
