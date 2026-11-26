---
title: "UseExternalStore"
description: "Subscribes to an external store that exposes a current snapshot getter. Re-renders only when a change notification yields a different snapshot."
---

```csharp
public TSnapshot UseExternalStore<TSnapshot>(Func<Action, Action> subscribe, Func<TSnapshot> getSnapshot, IEqualityComparer<TSnapshot>? comparer = null)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Reactivity Model](/microsoft-ui-reactor/main/how-it-works/reactivity-model), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `subscribe` | `Func<Action, Action>` |  |  |
| `getSnapshot` | `Func<TSnapshot>` |  |  |
| `comparer` | `IEqualityComparer<TSnapshot>?` | `null` |  |

## Remarks [#remarks]

`subscribe` is the effect dependency that decides whether the subscription is torn down and re-established, so it must be a **stable** delegate across renders. Pass a method group (e.g. `store.Subscribe`) or a [`UseCallback`](/microsoft-ui-reactor/main/reference/hooks/UseCallback#usecallbackaction-object)-memoized delegate. A fresh capturing lambda (`onChanged => store.Subscribe(onChanged)`) is a new delegate every render and will unsubscribe/resubscribe on each one. This mirrors React's guidance for `useSyncExternalStore`.

`getSnapshot` must return a **cached/stable** value that only changes identity when the underlying data changes. Returning a fresh, never-equal value on every call (e.g. `() => items.ToArray()`) combined with an unstable `subscribe` can spin: re-render re-runs the effect, the immediate re-check observes a "change", and that forces another render. Memoize the snapshot or return a value the supplied comparer treats as equal when nothing changed.

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected TSnapshot UseExternalStore<TSnapshot>(Func<Action, Action> subscribe, Func<TSnapshot> getSnapshot, IEqualityComparer<TSnapshot>? comparer = null)
```
