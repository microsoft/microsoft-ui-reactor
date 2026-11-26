# UseMemoCells

Memoize cell construction for `items`. On the first render the builder runs for every index; on subsequent renders, an item that compares [`Object.Equals`](https://learn.microsoft.com/dotnet/api/system.object.equals) against the previous render's value at the same index reuses the previous element. Any change to `dependencies` invalidates the entire cache and rebuilds every cell.

```csharp
public static Element[] UseMemoCells<T>(this RenderContext ctx, IReadOnlyList<T> items, Func<T, int, Element> builder, params object[] dependencies) where T : notnull
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseMemoCellsExtensions`.

**Learn more:** [Hooks](../../hooks.md), [Performance](../../performance.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IReadOnlyList<T>` | Source items, one cell per item. |
| `builder` | `Func<T, int, Element>` | Builder for a single cell. Must be a pure function of `(item, index)` plus `dependencies`. Closure captures missing from the deps list are flagged by the `REACTOR_HOOKS_007` analyzer. |
| `dependencies` | `object[]` | Trailing-`params` list of values the builder closes over. Equivalent semantics to `UseMemo`: any change invalidates the entire memo. |

## Type parameters

| Name | Description |
| --- | --- |
| `T` | Item type. |

## Remarks

Spec 034 §C.

## Example

```csharp
var scheme = ctx.UseColorScheme();
var children = ctx.UseMemoCells(
    stocks,
    (item, i) => Cell(item, scheme),
    scheme);   // ← deps; framework invalidates on change
```

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static Element[] UseMemoCells<T>(this Component component, IReadOnlyList<T> items, Func<T, int, Element> builder, params object[] dependencies) where T : notnull
```
