---
title: "UseReducer"
seo:
  description: "Declares a piece of state with a functional updater variant. The updater receives the previous value and returns the next. Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher (same semantics as UseState); pass threadSafe: true for locked in-place updates that serialize many concurrent writers without an intervening UI tick."
---

Declares a piece of state with a functional updater variant. The updater receives the previous value and returns the next. Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher (same semantics as [`UseState`](/microsoft-ui-reactor/main/reference/hooks/UseState)); pass `threadSafe`: `true` for locked in-place updates that serialize many concurrent writers without an intervening UI tick.

```csharp
public (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
public (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Overloads [#overloads]

| Overload | Description |
| --- | --- |
| [`UseReducer<T>(T, bool)`](#usereducertt-bool) | Declares a piece of state with a functional updater variant. |
| [`UseReducer<TState, TAction>(Func<TState, TAction, TState>, TState, bool)`](#usereducertstate-tactionfunctstate-taction-tstate-tstate-bool) | Declares a piece of state managed by a reducer function (like Redux). |

### `UseReducer<T>(T, bool)` [#usereducertt-bool]

Declares a piece of state with a functional updater variant. The updater receives the previous value and returns the next. Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher (same semantics as [`UseState`](/microsoft-ui-reactor/main/reference/hooks/UseState)); pass `threadSafe`: `true` for locked in-place updates that serialize many concurrent writers without an intervening UI tick.

```csharp
public (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `initialValue` | `T` |  |  |
| `threadSafe` | `bool` | `false` |  |

### `UseReducer<TState, TAction>(Func<TState, TAction, TState>, TState, bool)` [#usereducertstate-tactionfunctstate-taction-tstate-tstate-bool]

Declares a piece of state managed by a reducer function (like Redux). The reducer takes (currentState, action) and returns the next state. Returns (currentState, dispatch) where dispatch sends an action through the reducer. Cross-thread dispatch calls are auto-marshaled onto the captured UI dispatcher (same semantics as [`UseState`](/microsoft-ui-reactor/main/reference/hooks/UseState)); pass `threadSafe`: `true` for locked in-place dispatch that serializes concurrent writers without an intervening UI tick.

```csharp
public (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `reducer` | `Func<TState, TAction, TState>` |  |  |
| `initialValue` | `TState` |  |  |
| `threadSafe` | `bool` | `false` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
protected (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
```
