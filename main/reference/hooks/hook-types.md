---
title: "Hook results and options"
description: "Small types that hooks return or take as options."
---

**Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## `AnnounceHandle` [#announcehandle]

Handle returned by [`UseAnnounce`](/microsoft-ui-reactor/main/reference/hooks/UseAnnounce). Provides an imperative [`AnnounceHandle.Announce`](#announcestring) method for screen reader live-region announcements plus a zero-size [`AnnounceHandle.Region`](#region) element that must be included somewhere in the component's rendered tree.

```csharp
public sealed class AnnounceHandle
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Properties [#properties]

#### `Region` [#region]

A zero-size, invisible Reactor element that acts as the live-region anchor. Include this anywhere in your component tree (it renders nothing visible).

```csharp
public Element Region  // get
```

### Methods [#methods]

#### `Announce` [#announce]

##### `Announce(string)` [#announcestring]

Announces a message to screen readers (polite — queued after current speech).

```csharp
public void Announce(string message)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `message` | `string` |  |

##### `Announce(string, bool)` [#announcestring-bool]

Announces a message to screen readers.

```csharp
public void Announce(string message, bool assertive)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `message` | `string` | The text to announce. |
| `assertive` | `bool` | If true, interrupts current speech immediately. If false (default), queued after current speech finishes. |

**Remarks**

Thread-safe. When called on the UI thread the announcement is raised synchronously. When called from any other thread it is marshalled to the captured UI [`DispatcherQueue`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.dispatching.dispatcherqueue) and runs asynchronously (fire-and-forget) — it may complete after this method returns. Calls made before the live-region [`AnnounceHandle.Region`](#region) has mounted are ignored.

## `FocusTrapHandle` [#focustraphandle]

Handle returned by UseFocusTrap. When active, traps keyboard focus within the container element so Tab/Shift+Tab cycles within the trapped scope. Essential for modal dialogs and flyouts.

```csharp
public sealed class FocusTrapHandle
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors [#constructors]

#### `FocusTrapHandle` [#focustraphandle_1]

```csharp
public FocusTrapHandle()
```

### Properties [#properties_1]

#### `IsActive` [#isactive]

Whether the focus trap is currently active.

```csharp
public bool IsActive  // get
```

## `IHookDispatcher` [#ihookdispatcher]

Abstraction for marshalling continuations back to the render thread. Null-safe — if the callback is absent the continuation runs inline on the thread-pool completion thread (tests and headless hosts).

```csharp
public interface IHookDispatcher
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Remarks [#remarks]

The production implementation wraps `DispatcherQueue.TryEnqueue`; tests typically install a synchronous stub that records invocations.

### Methods [#methods_1]

#### `Post` [#post]

Enqueue `action` to run on the dispatcher thread.

```csharp
public void Post(Action action)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `action` | `Action` |  |

## `Mutation<TInput, TResult>` [#mutationtinput-tresult]

Handle returned by the `UseMutation` hook. Carries the pending/error/last-result state and the [`Mutation.RunAsync`](#runasync) entry point.

```csharp
public sealed class Mutation<TInput, TResult>
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Remarks [#remarks_1]

**Concurrency.** Overlapping [`Mutation.RunAsync`](#runasync) calls each get their own cancellation token; both complete and fire their callbacks in completion order. [`Mutation.LastResult`](#lastresult) is whichever finishes last. If you want strictly-serialized mutations, wrap [`Mutation.RunAsync`](#runasync) behind your own gate (or disable the trigger control while [`Mutation.IsPending`](#ispending) is true).

**Reset.** [`Mutation.Reset`](#reset) clears [`Mutation.Error`](#error) and [`Mutation.LastResult`](#lastresult) but does **not** cancel in-flight work — this is an explicit choice so a "dismiss the error banner" action doesn't abort the user's retry.

### Properties [#properties_2]

#### `Error` [#error]

Most recent error, or null if the last completion was a success or [`Mutation.Reset`](#reset) was called.

```csharp
public Exception? Error  // get
```

#### `IsPending` [#ispending]

True while at least one [`Mutation.RunAsync`](#runasync) call is in-flight.

```csharp
public bool IsPending  // get
```

#### `LastResult` [#lastresult]

Most recent successful result, or default if none yet.

```csharp
public TResult? LastResult  // get
```

### Methods [#methods_2]

#### `Reset` [#reset]

Clear [`Mutation.Error`](#error) and [`Mutation.LastResult`](#lastresult). Does not cancel or affect any in-flight [`Mutation.RunAsync`](#runasync) call.

```csharp
public void Reset()
```

#### `RunAsync` [#runasync]

Kick off the mutator with `input`. Returns a task that completes with the mutator's result or fault.

```csharp
public Task<TResult> RunAsync(TInput input)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `input` | `TInput` |  |

**Remarks**

Ordering: [`MutationOptions.OnOptimistic`](#onoptimistic) fires synchronously before the mutator starts. [`MutationOptions.OnSuccess`](#onsuccess) or [`MutationOptions.OnError`](#onerror) fires on the hook's dispatcher thread after completion.

If [`MutationOptions.OnOptimistic`](#onoptimistic) throws, the mutator is never invoked and the returned task is faulted with the optimistic exception — prevents half-applied state where the optimistic patch ran but the real request can't.

## `MutationOptions<TInput, TResult>` [#mutationoptionstinput-tresult]

Callbacks and side-effects for the `UseMutation` hook. All callbacks are optional; all run on the dispatcher thread except [`MutationOptions.OnOptimistic`](#onoptimistic) which runs synchronously on the caller of [`Mutation.RunAsync`](#runasync) — the typical case is a render-thread click handler, so the optimistic update lands in the very next frame without a dispatcher hop.

```csharp
public sealed record MutationOptions<TInput, TResult>(Action<TInput>? OnOptimistic = null, Action<TResult, TInput>? OnSuccess = null, Action<Exception, TInput>? OnError = null, string[]? InvalidateKeys = null)
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Remarks [#remarks_2]

**InvalidateKeys.** On success, each key is passed to [`QueryCache.Invalidate`](/microsoft-ui-reactor/main/reference/data/query-cache#invalidate). Sibling `UseResource` hooks subscribed to those keys will observe the invalidation and refetch on their next render.

Error path: [`MutationOptions.OnError`](#onerror) fires but [`MutationOptions.InvalidateKeys`](#invalidatekeys) does **not** — the assumption is the server state didn't change, so the cache is still valid.

### Constructors [#constructors_1]

#### `MutationOptions` [#mutationoptions]

Callbacks and side-effects for the `UseMutation` hook. All callbacks are optional; all run on the dispatcher thread except [`MutationOptions.OnOptimistic`](#onoptimistic) which runs synchronously on the caller of [`Mutation.RunAsync`](#runasync) — the typical case is a render-thread click handler, so the optimistic update lands in the very next frame without a dispatcher hop.

```csharp
public MutationOptions(Action<TInput>? OnOptimistic = null, Action<TResult, TInput>? OnSuccess = null, Action<Exception, TInput>? OnError = null, string[]? InvalidateKeys = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `OnOptimistic` | `Action<TInput>?` | `null` |  |
| `OnSuccess` | `Action<TResult, TInput>?` | `null` |  |
| `OnError` | `Action<Exception, TInput>?` | `null` |  |
| `InvalidateKeys` | `string[]?` | `null` |  |

**Remarks**

**InvalidateKeys.** On success, each key is passed to [`QueryCache.Invalidate`](/microsoft-ui-reactor/main/reference/data/query-cache#invalidate). Sibling `UseResource` hooks subscribed to those keys will observe the invalidation and refetch on their next render.

Error path: [`MutationOptions.OnError`](#onerror) fires but [`MutationOptions.InvalidateKeys`](#invalidatekeys) does **not** — the assumption is the server state didn't change, so the cache is still valid.

### Properties [#properties_3]

#### `InvalidateKeys` [#invalidatekeys]

```csharp
public string[]? InvalidateKeys  // get, init
```

#### `OnError` [#onerror]

```csharp
public Action<Exception, TInput>? OnError  // get, init
```

#### `OnOptimistic` [#onoptimistic]

```csharp
public Action<TInput>? OnOptimistic  // get, init
```

#### `OnSuccess` [#onsuccess]

```csharp
public Action<TResult, TInput>? OnSuccess  // get, init
```

## `ResourceOptions` [#resourceoptions]

Tuning for the `UseResource` hook. Defaults mirror TanStack Query — zero [`ResourceOptions.StaleTime`](#staletime) (always refetch-on-mount but dedup concurrent requests), five-minute [`ResourceOptions.CacheTime`](#cachetime).

```csharp
public sealed record ResourceOptions(TimeSpan? StaleTime = null, TimeSpan? CacheTime = null, int RetryCount = 0, bool RefetchOnMount = true, string? CacheKey = null, bool RefetchOnWindowFocus = false)
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors [#constructors_2]

#### `ResourceOptions` [#resourceoptions_1]

Tuning for the `UseResource` hook. Defaults mirror TanStack Query — zero [`ResourceOptions.StaleTime`](#staletime) (always refetch-on-mount but dedup concurrent requests), five-minute [`ResourceOptions.CacheTime`](#cachetime).

```csharp
public ResourceOptions(TimeSpan? StaleTime = null, TimeSpan? CacheTime = null, int RetryCount = 0, bool RefetchOnMount = true, string? CacheKey = null, bool RefetchOnWindowFocus = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `StaleTime` | `TimeSpan?` | `null` | Freshness interval; null uses zero. |
| `CacheTime` | `TimeSpan?` | `null` | Retention after the last subscriber leaves; null uses five minutes. |
| `RetryCount` | `int` | `0` | Number of retries after a failed fetch. |
| `RefetchOnMount` | `bool` | `true` | Whether to fetch on a cache miss. |
| `CacheKey` | `string?` | `null` | Overrides the generated cache key; null uses a key derived from deps. When set, the key identifies the query; deps changes alone do not cancel an in-flight request or start a new fetch. Include values that select different data in the key, for example `$"user:{userId}"`. |
| `RefetchOnWindowFocus` | `bool` | `false` | Whether to opt into window-focus revalidation. |

### Properties [#properties_4]

#### `CacheKey` [#cachekey]

Overrides the generated cache key; null uses a key derived from deps. When set, the key identifies the query; deps changes alone do not cancel an in-flight request or start a new fetch. Include values that select different data in the key, for example `$"user:{userId}"`.

```csharp
public string? CacheKey  // get, init
```

#### `CacheTime` [#cachetime]

Retention after the last subscriber leaves; null uses five minutes.

```csharp
public TimeSpan? CacheTime  // get, init
```

#### `RefetchOnMount` [#refetchonmount]

Whether to fetch on a cache miss.

```csharp
public bool RefetchOnMount  // get, init
```

#### `RefetchOnWindowFocus` [#refetchonwindowfocus]

Whether to opt into window-focus revalidation.

```csharp
public bool RefetchOnWindowFocus  // get, init
```

#### `RetryCount` [#retrycount]

Number of retries after a failed fetch.

```csharp
public int RetryCount  // get, init
```

#### `StaleTime` [#staletime]

Freshness interval; null uses zero.

```csharp
public TimeSpan? StaleTime  // get, init
```

### Fields [#fields]

#### `Default` [#default]

```csharp
public static readonly ResourceOptions Default
```
