---
title: "AppContexts"
description: "Static context registrations shared across the Reactor core. Install the cache via ReactorHost bootstrap (or override per test)."
---

```csharp
public static class AppContexts
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Context](/microsoft-ui-reactor/main/guides/context)

## Fields [#fields]

### `FocusRevalidation` [#focusrevalidation]

Ambient [`FocusRevalidationService`](/microsoft-ui-reactor/main/reference/data/FocusRevalidationService) bound to the root [`AppContexts.QueryCache`](#querycache). Hooks that opt in via `ResourceOptions.RefetchOnWindowFocus = true` enroll themselves so the service can invalidate their cache entry on window activation / resume.

```csharp
public static readonly Context<FocusRevalidationService?> FocusRevalidation
```

### `PendingScope` [#pendingscope]

Nearest ancestor [`PendingScope`](/microsoft-ui-reactor/main/reference/hooks/pending#pendingscope), or `null` at the root. The `Pending` element provides a fresh scope to its subtree; hooks that participate in Pending-style bubble-up read this via `UseContext`.

```csharp
public static readonly Context<PendingScope?> PendingScope
```

### `QueryCache` [#querycache]

The ambient [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache). The default value is a fresh cache instance installed at app root; tests can provide their own.

```csharp
public static readonly Context<QueryCache> QueryCache
```
