---
title: "UseEffect"
description: "Runs a side effect after render. The effect re-runs when any dependency changes. Pass an empty array for \"run once on mount\" semantics. Returns a cleanup action that runs before the next effect or on unmount."
---

```csharp
public void UseEffect(Action effect, params object[] dependencies)
public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
public void UseEffect<T1>(Action effect, T1 d1)
public void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
public void UseEffect<T1, T2>(Action effect, T1 d1, T2 d2)
public void UseEffect<T1, T2>(Func<Action> effectWithCleanup, T1 d1, T2 d2)
public void UseEffect<T1, T2, T3>(Action effect, T1 d1, T2 d2, T3 d3)
public void UseEffect<T1, T2, T3>(Func<Action> effectWithCleanup, T1 d1, T2 d2, T3 d3)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Effects and Lifecycle](/microsoft-ui-reactor/main/guides/effects), [Effects Scheduling](/microsoft-ui-reactor/main/how-it-works/effects-scheduling), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Overloads [#overloads]

| Overload | Description |
| --- | --- |
| [`UseEffect(Action, object[])`](#useeffectaction-object) | Runs a side effect after render. |
| [`UseEffect(Func<Action>, object[])`](#useeffectfuncaction-object) | Like UseEffect but the effect returns a cleanup function. |
| [`UseEffect<T1>(Action, T1)`](#useeffectt1action-t1) | Single-dependency `UseEffect` overload that avoids the `params object[]` allocation (and value-type boxing) on the deps-unchanged path. |
| [`UseEffect<T1>(Func<Action>, T1)`](#useeffectt1funcaction-t1) | Single-dependency cleanup-flavor `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. |
| [`UseEffect<T1, T2>(Action, T1, T2)`](#useeffectt1-t2action-t1-t2) | Two-dependency `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. |
| [`UseEffect<T1, T2>(Func<Action>, T1, T2)`](#useeffectt1-t2funcaction-t1-t2) | Two-dependency cleanup-flavor `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. |
| [`UseEffect<T1, T2, T3>(Action, T1, T2, T3)`](#useeffectt1-t2-t3action-t1-t2-t3) | Three-dependency `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. |
| [`UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)`](#useeffectt1-t2-t3funcaction-t1-t2-t3) | Three-dependency cleanup-flavor `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. |

### `UseEffect(Action, object[])` [#useeffectaction-object]

Runs a side effect after render. The effect re-runs when any dependency changes. Pass an empty array for "run once on mount" semantics. Returns a cleanup action that runs before the next effect or on unmount.

```csharp
public void UseEffect(Action effect, params object[] dependencies)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effect` | `Action` |  |
| `dependencies` | `object[]` |  |

### `UseEffect(Func<Action>, object[])` [#useeffectfuncaction-object]

Like UseEffect but the effect returns a cleanup function.

```csharp
public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effectWithCleanup` | `Func<Action>` |  |
| `dependencies` | `object[]` |  |

### `UseEffect<T1>(Action, T1)` [#useeffectt1action-t1]

Single-dependency `UseEffect` overload that avoids the `params object[]` allocation (and value-type boxing) on the deps-unchanged path. Semantically identical to the params overload called with one dependency: the effect re-runs only when `d1` changes.

```csharp
public void UseEffect<T1>(Action effect, T1 d1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effect` | `Action` |  |
| `d1` | `T1` |  |

**Remarks**

If `d1`'s compile-time type is an array of reference types (e.g. `string[]`), it is treated as a dependency *list* and compared element-wise — matching the `params object[]` overload — not as a single reference-compared value. A dependency whose static type is not an array is always compared as one value, even if its runtime value happens to be an array.

### `UseEffect<T1>(Func<Action>, T1)` [#useeffectt1funcaction-t1]

Single-dependency cleanup-flavor `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. Semantically identical to the params overload called with one dependency.

```csharp
public void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effectWithCleanup` | `Func<Action>` |  |
| `d1` | `T1` |  |

**Remarks**

If `d1`'s compile-time type is an array of reference types (e.g. `string[]`), it is treated as a dependency *list* and compared element-wise — matching the `params object[]` overload — not as a single reference-compared value. A dependency whose static type is not an array is always compared as one value, even if its runtime value happens to be an array.

### `UseEffect<T1, T2>(Action, T1, T2)` [#useeffectt1-t2action-t1-t2]

Two-dependency `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. Re-runs when either dependency changes.

```csharp
public void UseEffect<T1, T2>(Action effect, T1 d1, T2 d2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effect` | `Action` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |

### `UseEffect<T1, T2>(Func<Action>, T1, T2)` [#useeffectt1-t2funcaction-t1-t2]

Two-dependency cleanup-flavor `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path.

```csharp
public void UseEffect<T1, T2>(Func<Action> effectWithCleanup, T1 d1, T2 d2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effectWithCleanup` | `Func<Action>` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |

### `UseEffect<T1, T2, T3>(Action, T1, T2, T3)` [#useeffectt1-t2-t3action-t1-t2-t3]

Three-dependency `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path. Re-runs when any dependency changes.

```csharp
public void UseEffect<T1, T2, T3>(Action effect, T1 d1, T2 d2, T3 d3)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effect` | `Action` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |
| `d3` | `T3` |  |

### `UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)` [#useeffectt1-t2-t3funcaction-t1-t2-t3]

Three-dependency cleanup-flavor `UseEffect` overload that avoids the `params object[]` allocation on the deps-unchanged path.

```csharp
public void UseEffect<T1, T2, T3>(Func<Action> effectWithCleanup, T1 d1, T2 d2, T3 d3)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `effectWithCleanup` | `Func<Action>` |  |
| `d1` | `T1` |  |
| `d2` | `T2` |  |
| `d3` | `T3` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseEffect(Action effect, params object[] dependencies)
protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
protected void UseEffect<T1>(Action effect, T1 d1)
protected void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
protected void UseEffect<T1, T2>(Action effect, T1 d1, T2 d2)
protected void UseEffect<T1, T2>(Func<Action> effectWithCleanup, T1 d1, T2 d2)
protected void UseEffect<T1, T2, T3>(Action effect, T1 d1, T2 d2, T3 d3)
protected void UseEffect<T1, T2, T3>(Func<Action> effectWithCleanup, T1 d1, T2 d2, T3 d3)
```
