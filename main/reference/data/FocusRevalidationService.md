---
title: "FocusRevalidationService"
seo:
  description: "Central service that invalidates stale QueryCache entries when the app window is reactivated or resumed, so consumers that opted in via ResourceOptions.RefetchOnWindowFocus = true see fresh data after an Alt-Tab or resume. Disabled by default — see ReactorFeatureFlags.FocusRevalidation."
---

Central service that invalidates stale [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) entries when the app window is reactivated or resumed, so consumers that opted in via `ResourceOptions.RefetchOnWindowFocus = true` see fresh data after an Alt-Tab or resume. Disabled by default — see [`ReactorFeatureFlags.FocusRevalidation`](/microsoft-ui-reactor/main/reference/extensibility/ReactorFeatureFlags#focusrevalidation).

```csharp
public sealed class FocusRevalidationService
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Async Resources](/microsoft-ui-reactor/main/guides/async-resources)

## Remarks [#remarks]

**Per-resource enrollment.** Hooks that opt in call [`FocusRevalidationService.Enroll`](#enroll) with their cache key; the service tracks enrolled keys as a set and invalidates only those whose entry is past `StaleTime` at activation time. Non-enrolled keys are untouched.

**Throttling.** A default 30-second window between activation-driven revalidation sweeps prevents Alt-Tab thrashing from refetching on every transient focus event. Adjustable via [`FocusRevalidationService.ThrottleWindow`](#throttlewindow).

**Threading.** All members are thread-safe. Activation callbacks may fire on the UI thread or the dispatcher thread — the service does not marshal. Invalidation in turn fires `QueryCache.EntryChanged`, which the `UseResource` hook listens to and re-renders from.

## Constructors [#constructors]

### `FocusRevalidationService` [#focusrevalidationservice_1]

```csharp
public FocusRevalidationService(QueryCache cache)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cache` | [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) |  |

## Properties [#properties]

### `EnrolledCount` [#enrolledcount]

Diagnostic: current number of keys enrolled for focus revalidation.

```csharp
public int EnrolledCount  // get
```

### `ThrottleWindow` [#throttlewindow]

Minimum time between activation-driven revalidation sweeps. Defaults to 30s.

```csharp
public TimeSpan ThrottleWindow  // get, set
```

### `UtcNow` [#utcnow]

Clock override for deterministic tests.

```csharp
public Func<DateTime> UtcNow  // get, set
```

## Methods [#methods]

### `Enroll` [#enroll]

Enroll `key` in focus revalidation. Hooks call this when `ResourceOptions.RefetchOnWindowFocus = true`. Idempotent — re-enrolling the same key is a no-op.

```csharp
public void Enroll(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

### `RevalidateNow` [#revalidatenow]

Revalidate enrolled entries. Invalidates every enrolled key whose entry is past its `StaleTime`. Returns the keys that were invalidated.

```csharp
public IReadOnlyList<string> RevalidateNow()
```

**Remarks**

Short-circuits if the last call fired within [`FocusRevalidationService.ThrottleWindow`](#throttlewindow) — returns an empty list without touching the cache.

### `RevalidateNowForce` [#revalidatenowforce]

Forces a revalidation sweep, bypassing the throttle window. Diagnostic / test-only — production code paths should go through [`FocusRevalidationService.RevalidateNow`](#revalidatenow).

```csharp
public IReadOnlyList<string> RevalidateNowForce()
```

### `Unenroll` [#unenroll]

Remove `key` from the focus-revalidation set. Hooks call this on unmount or when their cache key changes.

```csharp
public void Unenroll(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
