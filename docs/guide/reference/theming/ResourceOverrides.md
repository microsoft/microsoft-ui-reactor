# ResourceOverrides

Immutable snapshot of per-control resource overrides built by [`ResourceBuilder`](ResourceBuilder.md). Contains both literal resource values and theme-reactive [`ThemeRef`](ThemeRef.md) entries that are re-resolved on theme change.

```csharp
public sealed record ResourceOverrides(IReadOnlyDictionary<string, object> Literals, IReadOnlyDictionary<string, ThemeRef> ThemeRefs)
```

Namespace: `Microsoft.UI.Reactor.Elements`.

**Learn more:** [Styling and Theming](../../styling.md)

## Constructors

### `ResourceOverrides`

Immutable snapshot of per-control resource overrides built by [`ResourceBuilder`](ResourceBuilder.md). Contains both literal resource values and theme-reactive [`ThemeRef`](ThemeRef.md) entries that are re-resolved on theme change.

```csharp
public ResourceOverrides(IReadOnlyDictionary<string, object> Literals, IReadOnlyDictionary<string, ThemeRef> ThemeRefs)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Literals` | `IReadOnlyDictionary<string, object>` |  |
| `ThemeRefs` | `IReadOnlyDictionary<string, ThemeRef>` |  |

## Properties

### `Literals`

```csharp
public IReadOnlyDictionary<string, object> Literals  // get, init
```

### `ThemeRefs`

```csharp
public IReadOnlyDictionary<string, ThemeRef> ThemeRefs  // get, init
```
