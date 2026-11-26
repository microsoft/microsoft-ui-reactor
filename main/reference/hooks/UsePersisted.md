---
title: "UsePersisted"
description: "Like UseState, but the value survives unmount/remount via an in-memory cache. On first mount, uses cached value if available, otherwise uses initialValue. Value is saved to cache on unmount."
---

```csharp
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Persistence](/microsoft-ui-reactor/main/guides/persistence), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Overloads [#overloads]

| Overload | Description |
| --- | --- |
| [`UsePersisted<T>(string, T)`](#usepersistedtstring-t) | Like UseState, but the value survives unmount/remount via an in-memory cache. |
| [`UsePersisted<T>(string, T, PersistedScope)`](#usepersistedtstring-t-persistedscope) | Persisted-state hook with explicit scope (spec 033 §2). |

### `UsePersisted<T>(string, T)` [#usepersistedtstring-t]

Like UseState, but the value survives unmount/remount via an in-memory cache. On first mount, uses cached value if available, otherwise uses initialValue. Value is saved to cache on unmount.

```csharp
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `initialValue` | `T` |  |

**Remarks**

Spec 033 §2. The cache is currently process-wide ([`ApplicationPersistedScope.Default`](/microsoft-ui-reactor/main/reference/hosting/persistence#default)) and bounded by an LRU policy. The two-arg form is flagged by `REACTOR_PERSIST_001` (`UsePersistedScopeAnalyzer`); new code should use the three-arg overload to make the intended scope explicit.

### `UsePersisted<T>(string, T, PersistedScope)` [#usepersistedtstring-t-persistedscope]

Persisted-state hook with explicit scope (spec 033 §2). Use [`PersistedScope.Window`](/microsoft-ui-reactor/main/reference/hosting/persistence#values) for state that should be bounded by the host's lifetime; [`PersistedScope.Application`](/microsoft-ui-reactor/main/reference/hosting/persistence#values) for process-wide state.

```csharp
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `initialValue` | `T` |  |
| `scope` | [`PersistedScope`](/microsoft-ui-reactor/main/reference/hosting/persistence#persistedscope) |  |

**Remarks**

[`PersistedScope.Window`](/microsoft-ui-reactor/main/reference/hosting/persistence#values) resolves to the [`ReactorWindow.PersistedScope`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#persistedscope) of the window whose host renders this component; content without an owning window (tray flyouts, `ReactorHostControl`, unit-test contexts) falls back to the process-wide scope. Two windows of the same component class therefore hold independent state under [`PersistedScope.Window`](/microsoft-ui-reactor/main/reference/hosting/persistence#values). (spec 036 §3.4 / §4.4 — closes spec 033 §7.5.)

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
```
