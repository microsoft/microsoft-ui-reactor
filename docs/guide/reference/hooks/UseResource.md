# UseResource

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseResourceExtensions.UseResource``1(Microsoft.UI.Reactor.Core.RenderContext,System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task{``0}},Microsoft.UI.Reactor.Core.QueryCache,System.Object[],Microsoft.UI.Reactor.Hooks.ResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Runs an async fetch keyed on <paramref name="deps" />, returning an
`AsyncValue`1` that tracks the fetch's lifecycle. The hook
owns the cancellation token, stores results in <paramref name="cache" />,
and re-renders when new results land.

## Discussion

<para><b>Sync-complete fast path.</b> If <paramref name="fetcher" /> returns an
already-completed task, this call returns <c>Data(result)</c> on the same render,
with no transient <c>Loading</c> flash.</para><para><b>Dispatcher.</b> The hook captures the dispatcher at registration time
(<c>DispatcherQueue.GetForCurrentThread()</c>). In unit tests without a WinUI
dispatcher, continuations run inline on the thread-pool thread that completed
the fetch.</para>

## Featured in

- [Effects](../../effects.md)
- [Hooks](../../hooks.md)

