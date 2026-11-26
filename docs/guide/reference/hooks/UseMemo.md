# UseMemo

Memoizes a computed value, recomputing only when dependencies change.

```csharp
public T UseMemo<T>(Func<T> factory, params object[] dependencies)
public T UseMemo<T, T1>(Func<T> factory, T1 d1)
public T UseMemo<T, T1, T2>(Func<T> factory, T1 d1, T2 d2)
public T UseMemo<T, T1, T2, T3>(Func<T> factory, T1 d1, T2 d2, T3 d3)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Hooks](../../hooks.md), [Performance](../../performance.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseMemo<T>(Func<T>, object[])`](#usememotfunct-object) | Memoizes a computed value, recomputing only when dependencies change. |
| [`UseMemo<T, T1>(Func<T>, T1)`](#usememot-t1funct-t1) | Single-dependency `UseMemo` overload that avoids the `params object[]` allocation (and value-type boxing) on the deps-unchanged path. |
| [`UseMemo<T, T1, T2>(Func<T>, T1, T2)`](#usememot-t1-t2funct-t1-t2) | Two-dependency `UseMemo` overload that avoids the `params object[]` allocation on the deps-unchanged path. |
| [`UseMemo<T, T1, T2, T3>(Func<T>, T1, T2, T3)`](#usememot-t1-t2-t3funct-t1-t2-t3) | Three-dependency `UseMemo` overload that avoids the `params object[]` allocation on the deps-unchanged path. |

### `UseMemo<T>(Func<T>, object[])`

Memoizes a computed value, recomputing only when dependencies change.

```csharp
public T UseMemo<T>(Func<T> factory, params object[] dependencies)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factory` | `Func<T>` |  |
| `dependencies` | `object[]` |  |

### `UseMemo<T, T1>(Func<T>, T1)`

Single-dependency `UseMemo` overload that avoids the `params object[]` allocation (and value-type boxing) on the deps-unchanged path. Recomputes only when `d1` changes.

```csharp
public T UseMemo<T, T1>(Func<T> factory, T1 d1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factory` | `Func<T>` |  |
| `d1` | `T1` |  |

**Remarks**

If `d1`'s compile-time type is an array of reference types (e.g. `string[]`), it is treated as a dependency *list* and compared element-wise — matching the `params object[]` overload — not as a single reference-compared value. A dependency whose static type is not an array is always compared as one value, even if its runtime value happens to be an array.

### `UseMemo<T, T1, T2>(Func<T>, T1, T2)`

Two-dependency `UseMemo` overload that avoids the `params object[]` allocation on the deps-unchanged path.

```csharp
public T UseMemo<T, T1, T2>(Func<T> factory, T1 d1, T2 d2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factory` | `Func<T>` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |

### `UseMemo<T, T1, T2, T3>(Func<T>, T1, T2, T3)`

Three-dependency `UseMemo` overload that avoids the `params object[]` allocation on the deps-unchanged path.

```csharp
public T UseMemo<T, T1, T2, T3>(Func<T> factory, T1 d1, T2 d2, T3 d3)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factory` | `Func<T>` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |
| `d3` | `T3` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected T UseMemo<T>(Func<T> factory, params object[] dependencies)
protected T UseMemo<T, T1>(Func<T> factory, T1 d1)
protected T UseMemo<T, T1, T2>(Func<T> factory, T1 d1, T2 d2)
protected T UseMemo<T, T1, T2, T3>(Func<T> factory, T1 d1, T2 d2, T3 d3)
```
