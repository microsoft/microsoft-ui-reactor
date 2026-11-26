---
title: "UseState"
description: "Declares a piece of state. Returns (currentValue, setter). Must be called in the same order every render (just like React hooks)."
---

When `threadSafe` is true, reads and writes are synchronized with a per-hook lock so concurrent setter calls from many threads serialize. When false (default), cross-thread setter calls are auto-marshaled onto the captured UI dispatcher — the write and the rerender both run on the UI thread, so background-thread setters from `Task.Run`, `PeriodicTimer`, or callbacks after `await … ConfigureAwait(false)` work correctly without any extra opt-in. Use `threadSafe`: `true` when you need many concurrent setters to apply in-place (i.e., without an intervening UI thread hop) or when the setter result must be visible to its caller before the next UI tick.

```csharp
public (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `initialValue` | `T` |  |  |
| `threadSafe` | `bool` | `false` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
```
