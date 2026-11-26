# Icon data

Records that describe an icon.

**Learn more:** [Controls](../../controls.md)

## `BitmapIconData`

```csharp
public record BitmapIconData(Uri Source, bool ShowAsMonochrome = true) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors

#### `BitmapIconData`

```csharp
public BitmapIconData(Uri Source, bool ShowAsMonochrome = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Source` | `Uri` |  |  |
| `ShowAsMonochrome` | `bool` | `true` |  |

### Properties

#### `ShowAsMonochrome`

```csharp
public bool ShowAsMonochrome  // get, init
```

#### `Source`

```csharp
public Uri Source  // get, init
```

## `FontIconData`

```csharp
public record FontIconData(string Glyph, string? FontFamily = null, double? FontSize = null) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors

#### `FontIconData`

```csharp
public FontIconData(string Glyph, string? FontFamily = null, double? FontSize = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Glyph` | `string` |  |  |
| `FontFamily` | `string?` | `null` |  |
| `FontSize` | `double?` | `null` |  |

### Properties

#### `FontFamily`

```csharp
public string? FontFamily  // get, init
```

#### `FontSize`

```csharp
public double? FontSize  // get, init
```

#### `Glyph`

```csharp
public string Glyph  // get, init
```

## `IconData`

```csharp
public abstract record IconData
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `IconData`

```csharp
protected IconData()
```

## `ImageIconData`

```csharp
public record ImageIconData(Uri Source) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors

#### `ImageIconData`

```csharp
public ImageIconData(Uri Source)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Source` | `Uri` |  |

### Properties

#### `Source`

```csharp
public Uri Source  // get, init
```

## `PathIconData`

```csharp
public record PathIconData(string Data) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors

#### `PathIconData`

```csharp
public PathIconData(string Data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Data` | `string` |  |

### Properties

#### `Data`

```csharp
public string Data  // get, init
```

## `SymbolIconData`

```csharp
public record SymbolIconData(string Symbol) : IconData
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`IconData`](#icondata).

### Constructors

#### `SymbolIconData`

```csharp
public SymbolIconData(string Symbol)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Symbol` | `string` |  |

### Properties

#### `Symbol`

```csharp
public string Symbol  // get, init
```
