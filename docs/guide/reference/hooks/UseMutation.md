# UseMutation

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMutationExtensions.UseMutation``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``0,System.Threading.CancellationToken,System.Threading.Tasks.Task{``1}},Microsoft.UI.Reactor.Hooks.MutationOptions{``0,``1},Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseMutation<T1, T2>(RenderContext, Func<T1, CancellationToken, Task<T2>>, MutationOptions<T1, T2>, IHookDispatcher)`](#usemutationt1-t2rendercontext-funct1-cancellationtoken-taskt2-mutationoptionst1-t2-ihookdispatcher)
- [`UseMutation<T1, T2>(RenderContext, Func<T1, CancellationToken, Task<T2>>, QueryCache, MutationOptions<T1, T2>, IHookDispatcher)`](#usemutationt1-t2rendercontext-funct1-cancellationtoken-taskt2-querycache-mutationoptionst1-t2-ihookdispatcher)

## `UseMutation<T1, T2>(RenderContext, Func<T1, CancellationToken, Task<T2>>, MutationOptions<T1, T2>, IHookDispatcher)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMutationExtensions.UseMutation``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``0,System.Threading.CancellationToken,System.Threading.Tasks.Task{``1}},Microsoft.UI.Reactor.Hooks.MutationOptions{``0,``1},Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

### Summary

Overload that reads the ambient `QueryCache` from
`AppContexts.QueryCache`.

## `UseMutation<T1, T2>(RenderContext, Func<T1, CancellationToken, Task<T2>>, QueryCache, MutationOptions<T1, T2>, IHookDispatcher)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMutationExtensions.UseMutation``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``0,System.Threading.CancellationToken,System.Threading.Tasks.Task{``1}},Microsoft.UI.Reactor.Core.QueryCache,Microsoft.UI.Reactor.Hooks.MutationOptions{``0,``1},Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

### Summary

Registers a [Mutation](Mutation.md) ([guide](../../hooks.md)) for this hook slot. The handle
is stable across renders (pass it to buttons, context menus, etc.).

### Parameters

- **ctx** — The render context (extension target).
- **mutator** — The async write. Receives the caller's input and a token that
fires on unmount. Rethrow `OperationCanceledException` to honour it.
- **cache** — The cache whose keys to invalidate on success, or null to skip
invalidation regardless of [InvalidateKeys](MutationOptions.md).
- **options** — Optional lifecycle callbacks; null uses defaults (no callbacks).
- **dispatcher** — Optional dispatcher override; null captures the current
`DispatcherQueue` at registration time (same convention as `UseResource`).


