# AppContexts

Static context registrations shared across the Reactor core. Install the cache via `ReactorHost` bootstrap (or override per test).

```csharp
public static class AppContexts
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Context](../../context.md)

## Fields

### `FocusRevalidation`

Ambient [`FocusRevalidationService`](../data/FocusRevalidationService.md) bound to the root [`AppContexts.QueryCache`](#querycache). Hooks that opt in via `ResourceOptions.RefetchOnWindowFocus = true` enroll themselves so the service can invalidate their cache entry on window activation / resume.

```csharp
public static readonly Context<FocusRevalidationService?> FocusRevalidation
```

### `PendingScope`

Nearest ancestor [`PendingScope`](../hooks/pending.md#pendingscope), or `null` at the root. The `Pending` element provides a fresh scope to its subtree; hooks that participate in Pending-style bubble-up read this via `UseContext`.

```csharp
public static readonly Context<PendingScope?> PendingScope
```

### `QueryCache`

The ambient [`QueryCache`](../data/query-cache.md#querycache). The default value is a fresh cache instance installed at app root; tests can provide their own.

```csharp
public static readonly Context<QueryCache> QueryCache
```
