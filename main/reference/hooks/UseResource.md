---
title: "UseResource"
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Overloads [#overloads]

- [`UseResource<T1>(RenderContext, Func<CancellationToken, Task<T1>>, object[], ResourceOptions, IHookDispatcher)`](#useresourcet1rendercontext-funccancellationtoken-taskt1-object-resourceoptions-ihookdispatcher)
- [`UseResource<T1>(RenderContext, Func<CancellationToken, Task<T1>>, QueryCache, object[], ResourceOptions, IHookDispatcher)`](#useresourcet1rendercontext-funccancellationtoken-taskt1-querycache-object-resourceoptions-ihookdispatcher)

## `UseResource<T1>(RenderContext, Func<CancellationToken, Task<T1>>, object[], ResourceOptions, IHookDispatcher)` [#useresourcet1rendercontext-funccancellationtoken-taskt1-object-resourceoptions-ihookdispatcher]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseResourceExtensions.UseResource``1(Microsoft.UI.Reactor.Core.RenderContext,System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task{``0}},System.Object[],Microsoft.UI.Reactor.Hooks.ResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

### Summary [#summary]

Overload that reads the ambient `QueryCache` from
`AppContexts.QueryCache`. `ReactorHost` installs a
process-wide default cache at startup; tests or subtrees may override it via
`.Provide(AppContexts.QueryCache, customCache)`.

## `UseResource<T1>(RenderContext, Func<CancellationToken, Task<T1>>, QueryCache, object[], ResourceOptions, IHookDispatcher)` [#useresourcet1rendercontext-funccancellationtoken-taskt1-querycache-object-resourceoptions-ihookdispatcher]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseResourceExtensions.UseResource``1(Microsoft.UI.Reactor.Core.RenderContext,System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task{``0}},Microsoft.UI.Reactor.Core.QueryCache,System.Object[],Microsoft.UI.Reactor.Hooks.ResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher)`

### Summary [#summary_1]

Runs an async fetch keyed on `deps`, returning an
`AsyncValue` that tracks the fetch's lifecycle. The hook
owns the cancellation token, stores results in `cache`,
and re-renders when new results land.

### Discussion [#discussion]



**Sync-complete fast path.** If `fetcher` returns an
already-completed task, this call returns `Data(result)` on the same render,
with no transient `Loading` flash.



**Dispatcher.** The hook captures the dispatcher at registration time
(`DispatcherQueue.GetForCurrentThread()`). In unit tests without a WinUI
dispatcher, continuations run inline on the thread-pool thread that completed
the fetch.

## Featured in [#featured-in]

- [Effects](/microsoft-ui-reactor/main/guides/effects)
- [Hooks](/microsoft-ui-reactor/main/guides/hooks)
