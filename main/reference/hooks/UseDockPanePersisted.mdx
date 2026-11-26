---
title: "UseDockPanePersisted"
seo:
  description: "Per-pane persisted state. Auto-prefixes the supplied key with pane:<paneKey>: so two panes that share a key name (e.g. \"scrollOffset\") get independent stored values instead of trampling each other in the underlying WindowPersistedScope."
---

Per-pane persisted state. Auto-prefixes the supplied key with `pane:<paneKey>:` so two panes that share a key name (e.g. `"scrollOffset"`) get independent stored values instead of trampling each other in the underlying [`WindowPersistedScope`](/microsoft-ui-reactor/main/reference/hosting/persistence#windowpersistedscope).

```csharp
public static (T Value, Action<T> Set) UseDockPanePersisted<T>(this RenderContext ctx, string key, T initialValue)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `initialValue` | `T` |  |

## Remarks [#remarks]

Spec 045 §2.9 + §2.24. Resolves the enclosing pane via [`UsePane`](/microsoft-ui-reactor/main/reference/hooks/UsePane); throws when called outside a pane subtree (same contract as `UsePane`). Internally forwards to [`UsePersisted`](/microsoft-ui-reactor/main/reference/hooks/UsePersisted#usepersistedtstring-t-persistedscope) with [`PersistedScope.Window`](/microsoft-ui-reactor/main/reference/hosting/persistence#values) so state lives for the duration of the host window and is dropped on window unload.

Cross-user-secret caveat (spec §8.4 / §8.9): apps storing sensitive per-pane data should clear it explicitly on logout / scope change. The pane scope itself does not bind to user identity.
