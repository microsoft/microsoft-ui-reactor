# UseMutation

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMutationExtensions.UseMutation``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``0,System.Threading.CancellationToken,System.Threading.Tasks.Task{``1}},Microsoft.UI.Reactor.Core.QueryCache,Microsoft.UI.Reactor.Hooks.MutationOptions{``0,``1},Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Registers a [Mutation](Mutation.md) ([guide](../../hooks.md)) for this hook slot. The handle
is stable across renders (pass it to buttons, context menus, etc.).

## Parameters

- **ctx** — The render context (extension target).
- **mutator** — The async write. Receives the caller's input and a token that
fires on unmount. Rethrow `OperationCanceledException` to honour it.
- **cache** — The cache whose keys to invalidate on success, or null to skip
invalidation regardless of `InvalidateKeys`.
- **options** — Optional lifecycle callbacks; null uses defaults (no callbacks).
- **dispatcher** — Optional dispatcher override; null captures the current
<c>DispatcherQueue</c> at registration time (same convention as <c>UseResource</c>).


