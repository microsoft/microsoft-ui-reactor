# UseCallback

Returns a stable callback reference that doesn't change between renders.

```csharp
public Action UseCallback(Action callback, params object[] dependencies)
public Action UseCallback<T1>(Action callback, T1 d1)
public Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
public Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Hooks](../../hooks.md), [Performance](../../performance.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseCallback(Action, object[])`](#usecallbackaction-object) | Returns a stable callback reference that doesn't change between renders. |
| [`UseCallback<T1>(Action, T1)`](#usecallbackt1action-t1) | Single-dependency `UseCallback` overload that avoids the `params object[]` allocation (and value-type boxing) on the deps-unchanged path. |
| [`UseCallback<T1, T2>(Action, T1, T2)`](#usecallbackt1-t2action-t1-t2) | Two-dependency `UseCallback` overload that avoids the `params object[]` allocation on the deps-unchanged path. |
| [`UseCallback<T1, T2, T3>(Action, T1, T2, T3)`](#usecallbackt1-t2-t3action-t1-t2-t3) | Three-dependency `UseCallback` overload that avoids the `params object[]` allocation on the deps-unchanged path. |

### `UseCallback(Action, object[])`

Returns a stable callback reference that doesn't change between renders.

```csharp
public Action UseCallback(Action callback, params object[] dependencies)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action` |  |
| `dependencies` | `object[]` |  |

### `UseCallback<T1>(Action, T1)`

Single-dependency `UseCallback` overload that avoids the `params object[]` allocation (and value-type boxing) on the deps-unchanged path. Returns a stable reference until `d1` changes.

```csharp
public Action UseCallback<T1>(Action callback, T1 d1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action` |  |
| `d1` | `T1` |  |

**Remarks**

If `d1`'s compile-time type is an array of reference types (e.g. `string[]`), it is treated as a dependency *list* and compared element-wise — matching the `params object[]` overload — not as a single reference-compared value. A dependency whose static type is not an array is always compared as one value, even if its runtime value happens to be an array.

### `UseCallback<T1, T2>(Action, T1, T2)`

Two-dependency `UseCallback` overload that avoids the `params object[]` allocation on the deps-unchanged path.

```csharp
public Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |

### `UseCallback<T1, T2, T3>(Action, T1, T2, T3)`

Three-dependency `UseCallback` overload that avoids the `params object[]` allocation on the deps-unchanged path.

```csharp
public Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |
| `d3` | `T3` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected Action UseCallback(Action callback, params object[] dependencies)
protected Action UseCallback<T1>(Action callback, T1 d1)
protected Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
protected Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
```
