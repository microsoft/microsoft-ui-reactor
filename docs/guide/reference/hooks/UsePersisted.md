# UsePersisted

Like UseState, but the value survives unmount/remount via an in-memory cache. On first mount, uses cached value if available, otherwise uses initialValue. Value is saved to cache on unmount.

```csharp
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Persistence](../../persistence.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UsePersisted<T>(string, T)`](#usepersistedtstring-t) | Like UseState, but the value survives unmount/remount via an in-memory cache. |
| [`UsePersisted<T>(string, T, PersistedScope)`](#usepersistedtstring-t-persistedscope) | Persisted-state hook with explicit scope (spec 033 §2). |

### `UsePersisted<T>(string, T)`

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

Spec 033 §2. The cache is currently process-wide ([`ApplicationPersistedScope.Default`](../hosting/persistence.md#default)) and bounded by an LRU policy. The two-arg form is flagged by `REACTOR_PERSIST_001` (`UsePersistedScopeAnalyzer`); new code should use the three-arg overload to make the intended scope explicit.

### `UsePersisted<T>(string, T, PersistedScope)`

Persisted-state hook with explicit scope (spec 033 §2). Use [`PersistedScope.Window`](../hosting/persistence.md#values) for state that should be bounded by the host's lifetime; [`PersistedScope.Application`](../hosting/persistence.md#values) for process-wide state.

```csharp
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `initialValue` | `T` |  |
| `scope` | [`PersistedScope`](../hosting/persistence.md#persistedscope) |  |

**Remarks**

[`PersistedScope.Window`](../hosting/persistence.md#values) resolves to the [`ReactorWindow.PersistedScope`](../hosting/ReactorWindow.md#persistedscope) of the window whose host renders this component; content without an owning window (tray flyouts, `ReactorHostControl`, unit-test contexts) falls back to the process-wide scope. Two windows of the same component class therefore hold independent state under [`PersistedScope.Window`](../hosting/persistence.md#values). (spec 036 §3.4 / §4.4 — closes spec 033 §7.5.)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
```
