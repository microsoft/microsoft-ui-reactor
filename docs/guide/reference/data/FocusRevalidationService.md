# FocusRevalidationService

Central service that invalidates stale [`QueryCache`](query-cache.md#querycache) entries when the app window is reactivated or resumed, so consumers that opted in via `ResourceOptions.RefetchOnWindowFocus = true` see fresh data after an Alt-Tab or resume. Disabled by default — see [`ReactorFeatureFlags.FocusRevalidation`](../extensibility/ReactorFeatureFlags.md#focusrevalidation).

```csharp
public sealed class FocusRevalidationService
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Async Resources](../../async-resources.md)

## Remarks

**Per-resource enrollment.** Hooks that opt in call [`FocusRevalidationService.Enroll`](#enroll) with their cache key; the service tracks enrolled keys as a set and invalidates only those whose entry is past `StaleTime` at activation time. Non-enrolled keys are untouched.

**Throttling.** A default 30-second window between activation-driven revalidation sweeps prevents Alt-Tab thrashing from refetching on every transient focus event. Adjustable via [`FocusRevalidationService.ThrottleWindow`](#throttlewindow).

**Threading.** All members are thread-safe. Activation callbacks may fire on the UI thread or the dispatcher thread — the service does not marshal. Invalidation in turn fires `QueryCache.EntryChanged`, which the `UseResource` hook listens to and re-renders from.

## Constructors

### `FocusRevalidationService`

```csharp
public FocusRevalidationService(QueryCache cache)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cache` | [`QueryCache`](query-cache.md#querycache) |  |

## Properties

### `EnrolledCount`

Diagnostic: current number of keys enrolled for focus revalidation.

```csharp
public int EnrolledCount  // get
```

### `ThrottleWindow`

Minimum time between activation-driven revalidation sweeps. Defaults to 30s.

```csharp
public TimeSpan ThrottleWindow  // get, set
```

### `UtcNow`

Clock override for deterministic tests.

```csharp
public Func<DateTime> UtcNow  // get, set
```

## Methods

### `Enroll`

Enroll `key` in focus revalidation. Hooks call this when `ResourceOptions.RefetchOnWindowFocus = true`. Idempotent — re-enrolling the same key is a no-op.

```csharp
public void Enroll(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

### `RevalidateNow`

Revalidate enrolled entries. Invalidates every enrolled key whose entry is past its `StaleTime`. Returns the keys that were invalidated.

```csharp
public IReadOnlyList<string> RevalidateNow()
```

**Remarks**

Short-circuits if the last call fired within [`FocusRevalidationService.ThrottleWindow`](#throttlewindow) — returns an empty list without touching the cache.

### `RevalidateNowForce`

Forces a revalidation sweep, bypassing the throttle window. Diagnostic / test-only — production code paths should go through [`FocusRevalidationService.RevalidateNow`](#revalidatenow).

```csharp
public IReadOnlyList<string> RevalidateNowForce()
```

### `Unenroll`

Remove `key` from the focus-revalidation set. Hooks call this on unmount or when their cache key changes.

```csharp
public void Unenroll(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
