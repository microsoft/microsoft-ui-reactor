# PathDataParser

Parses SVG path data strings into WinUI PathGeometry. General-purpose SVG-path parsing — lives in core so the Path control and other consumers can use it without the core depending on the Charting subsystem. Handles the subset of commands output by D3's PathBuilder.

```csharp
public static class PathDataParser
```

Namespace: `Microsoft.UI.Reactor.Core`.

## Methods

### `Parse`

```csharp
public static Geometry Parse(string pathData)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `pathData` | `string` |  |
