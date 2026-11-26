---
title: "UseMemoCellsByKey"
description: "Memoize cell construction keyed by keySelector. Cells are reused when both the item's key and value match the previous render. Keys that recur with mutated content rebuild that cell only. Reordered keys reuse cells (the reconciler's keyed- children path keeps the underlying control without unmount/remount)."
---

```csharp
public static Element[] UseMemoCellsByKey<T, TKey>(this RenderContext ctx, IReadOnlyList<T> items, Func<T, TKey> keySelector, Func<T, int, Element> builder, params object[] dependencies) where T : notnull where TKey : notnull
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `UseMemoCellsExtensions`.

**Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Performance](/microsoft-ui-reactor/main/guides/performance)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IReadOnlyList<T>` | Source items. |
| `keySelector` | `Func<T, TKey>` | Stable identity per item. Duplicate keys collapse to last-write-wins (later items overwrite earlier items in the lookup table). |
| `builder` | `Func<T, int, Element>` | Cell builder; same contract as [`UseMemoCells`](/microsoft-ui-reactor/main/reference/hooks/UseMemoCells). |
| `dependencies` | `object[]` | Trailing-`params` deps. |

## Remarks [#remarks]

Spec 034 §C.

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static Element[] UseMemoCellsByKey<T, TKey>(this Component component, IReadOnlyList<T> items, Func<T, TKey> keySelector, Func<T, int, Element> builder, params object[] dependencies) where T : notnull where TKey : notnull
```
