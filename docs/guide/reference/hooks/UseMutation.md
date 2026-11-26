# UseMutation

Registers a [`Mutation<TInput, TResult>`](hook-types.md#mutationtinput-tresult) for this hook slot. The handle is stable across renders (pass it to buttons, context menus, etc.).

```csharp
public static Mutation<TInput, TResult> UseMutation<TInput, TResult>(this RenderContext ctx, Func<TInput, CancellationToken, Task<TResult>> mutator, MutationOptions<TInput, TResult>? options = null, IHookDispatcher? dispatcher = null)
public static Mutation<TInput, TResult> UseMutation<TInput, TResult>(this RenderContext ctx, Func<TInput, CancellationToken, Task<TResult>> mutator, QueryCache? cache, MutationOptions<TInput, TResult>? options = null, IHookDispatcher? dispatcher = null)
protected Mutation<TInput, TResult> UseMutation<TInput, TResult>(Func<TInput, CancellationToken, Task<TResult>> mutator, MutationOptions<TInput, TResult>? options = null)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseMutationExtensions`.

**Learn more:** [Async Resources](../../async-resources.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseMutation<TInput, TResult>(RenderContext, Func<TInput, CancellationToken, Task<TResult>>, MutationOptions<TInput, TResult>?, IHookDispatcher?)`](#usemutationtinput-tresultrendercontext-functinput-cancellationtoken-tasktresult-mutationoptionstinput-tresult-ihookdispatcher) | Overload that reads the ambient `QueryCache` from `AppContexts.QueryCache`. |
| [`UseMutation<TInput, TResult>(RenderContext, Func<TInput, CancellationToken, Task<TResult>>, QueryCache?, MutationOptions<TInput, TResult>?, IHookDispatcher?)`](#usemutationtinput-tresultrendercontext-functinput-cancellationtoken-tasktresult-querycache-mutationoptionstinput-tresult-ihookdispatcher) | Registers a `Mutation<TInput, TResult>` for this hook slot. |
| [`UseMutation<TInput, TResult>(Func<TInput, CancellationToken, Task<TResult>>, MutationOptions<TInput, TResult>?)`](#usemutationtinput-tresultfunctinput-cancellationtoken-tasktresult-mutationoptionstinput-tresult) |  |

### `UseMutation<TInput, TResult>(RenderContext, Func<TInput, CancellationToken, Task<TResult>>, MutationOptions<TInput, TResult>?, IHookDispatcher?)`

Overload that reads the ambient [`QueryCache`](../data/query-cache.md#querycache) from [`AppContexts.QueryCache`](../components/AppContexts.md#querycache).

```csharp
public static Mutation<TInput, TResult> UseMutation<TInput, TResult>(this RenderContext ctx, Func<TInput, CancellationToken, Task<TResult>> mutator, MutationOptions<TInput, TResult>? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mutator` | `Func<TInput, CancellationToken, Task<TResult>>` |  |  |
| `options` | [`MutationOptions<TInput, TResult>?`](hook-types.md#mutationoptionstinput-tresult) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](hook-types.md#ihookdispatcher) | `null` |  |

### `UseMutation<TInput, TResult>(RenderContext, Func<TInput, CancellationToken, Task<TResult>>, QueryCache?, MutationOptions<TInput, TResult>?, IHookDispatcher?)`

Registers a [`Mutation<TInput, TResult>`](hook-types.md#mutationtinput-tresult) for this hook slot. The handle is stable across renders (pass it to buttons, context menus, etc.).

```csharp
public static Mutation<TInput, TResult> UseMutation<TInput, TResult>(this RenderContext ctx, Func<TInput, CancellationToken, Task<TResult>> mutator, QueryCache? cache, MutationOptions<TInput, TResult>? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mutator` | `Func<TInput, CancellationToken, Task<TResult>>` |  | The async write. Receives the caller's input and a token that fires on unmount. Rethrow [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) to honour it. |
| `cache` | [`QueryCache?`](../data/query-cache.md#querycache) |  | The cache whose keys to invalidate on success, or null to skip invalidation regardless of [`MutationOptions.InvalidateKeys`](hook-types.md#invalidatekeys). |
| `options` | [`MutationOptions<TInput, TResult>?`](hook-types.md#mutationoptionstinput-tresult) | `null` | Optional lifecycle callbacks; null uses defaults (no callbacks). |
| `dispatcher` | [`IHookDispatcher?`](hook-types.md#ihookdispatcher) | `null` | Optional dispatcher override; null captures the current `DispatcherQueue` at registration time (same convention as `UseResource`). |

### `UseMutation<TInput, TResult>(Func<TInput, CancellationToken, Task<TResult>>, MutationOptions<TInput, TResult>?)`

```csharp
protected Mutation<TInput, TResult> UseMutation<TInput, TResult>(Func<TInput, CancellationToken, Task<TResult>> mutator, MutationOptions<TInput, TResult>? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mutator` | `Func<TInput, CancellationToken, Task<TResult>>` |  |  |
| `options` | [`MutationOptions<TInput, TResult>?`](hook-types.md#mutationoptionstinput-tresult) | `null` |  |
