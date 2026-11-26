# UseDockPanePersisted

Per-pane persisted state. Auto-prefixes the supplied key with `pane:<paneKey>:` so two panes that share a key name (e.g. `"scrollOffset"`) get independent stored values instead of trampling each other in the underlying [`WindowPersistedScope`](../hosting/persistence.md#windowpersistedscope).

```csharp
public static (T Value, Action<T> Set) UseDockPanePersisted<T>(this RenderContext ctx, string key, T initialValue)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `initialValue` | `T` |  |

## Remarks

Spec 045 §2.9 + §2.24. Resolves the enclosing pane via [`UsePane`](UsePane.md); throws when called outside a pane subtree (same contract as `UsePane`). Internally forwards to [`UsePersisted`](UsePersisted.md#usepersistedtstring-t-persistedscope) with [`PersistedScope.Window`](../hosting/persistence.md#values) so state lives for the duration of the host window and is dropped on window unload.

Cross-user-secret caveat (spec §8.4 / §8.9): apps storing sensitive per-pane data should clear it explicitly on logout / scope change. The pane scope itself does not bind to user identity.
