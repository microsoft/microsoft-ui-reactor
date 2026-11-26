---
title: "Enums"
description: "The enumerations in this category."
---

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## `NavigationCacheMode` [#navigationcachemode]

Controls whether a page's component tree is cached across navigations.

```csharp
public enum NavigationCacheMode
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Disabled` | `0` | Page is always unmounted on navigate-away and remounted on navigate-to. |
| `Enabled` | `1` | Page is cached up to the host's CacheSize limit (LRU eviction). |
| `Required` | `2` | Page is always cached and never evicted by LRU. |

## `NavigationMode` [#navigationmode]

Represents the direction/type of a navigation operation.

```csharp
public enum NavigationMode
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Push` | `0` |  |
| `Pop` | `1` |  |
| `Replace` | `2` |  |
| `Reset` | `3` |  |
| `Forward` | `4` |  |
