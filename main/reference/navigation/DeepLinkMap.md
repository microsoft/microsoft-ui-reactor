---
title: "DeepLinkMap<TRoute>"
description: "Maps URI patterns to route constructors for deep linking. Patterns use /segment/{param:type} syntax where type is int or string."
sidebar:
  label: "DeepLinkMap"
---

```csharp
public sealed class DeepLinkMap<TRoute> where TRoute : notnull
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## Constructors [#constructors]

### `DeepLinkMap` [#deeplinkmap]

```csharp
public DeepLinkMap()
```

## Methods [#methods]

### `Map` [#map]

```csharp
public DeepLinkMap<TRoute> Map(string pattern, Func<RouteArgs, TRoute> factory)
public DeepLinkMap<TRoute> Map(string pattern, Func<RouteArgs, TRoute> factory, Func<TRoute[]> backStackFactory)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `pattern` | `string` |  |
| `factory` | `Func<RouteArgs, TRoute>` |  |
| `backStackFactory` | `Func<TRoute[]>` |  |

### `Resolve` [#resolve]

```csharp
public DeepLinkResult<TRoute> Resolve(string path)
public DeepLinkResult<TRoute> Resolve(Uri uri)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` |  |
| `uri` | `Uri` |  |
