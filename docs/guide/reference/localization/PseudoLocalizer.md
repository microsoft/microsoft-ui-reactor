# PseudoLocalizer

Transforms strings for pseudolocalization testing: - Replaces ASCII chars with accented equivalents - Wraps in \[!! ... !!\] markers - Pads ~30% longer to simulate text expansion - Preserves ICU syntax (\{variables\}, \{count, plural, ...\}) untouched

```csharp
public static class PseudoLocalizer
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Methods

### `MissingKeyMarker`

Returns the missing key pseudolocalization marker.

```csharp
public static string MissingKeyMarker(MessageKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`MessageKey`](MessageKey.md) |  |

### `Transform`

Pseudolocalizes a string, preserving ICU syntax.

```csharp
public static string Transform(string input)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `input` | `string` |  |
