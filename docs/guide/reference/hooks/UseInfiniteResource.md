# UseInfiniteResource

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseInfiniteResourceExtensions.UseInfiniteResource``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``1,System.Threading.CancellationToken,System.Threading.Tasks.Task{Microsoft.UI.Reactor.Core.Page{``0,``1}}},System.Object[],Microsoft.UI.Reactor.Core.InfiniteResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher,System.Func{System.Int32,``1})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseInfiniteResource<T1, T2>(RenderContext, Func<T2, CancellationToken, Task<Page<T1, T2>>>, object[], InfiniteResourceOptions, IHookDispatcher, Func<int, T2>)`](#useinfiniteresourcet1-t2rendercontext-funct2-cancellationtoken-taskpaget1-t2-object-infiniteresourceoptions-ihookdispatcher-funcint-t2)
- [`UseInfiniteResource<T1, T2>(RenderContext, Func<T2, CancellationToken, Task<Page<T1, T2>>>, QueryCache, object[], InfiniteResourceOptions, IHookDispatcher, Func<int, T2>)`](#useinfiniteresourcet1-t2rendercontext-funct2-cancellationtoken-taskpaget1-t2-querycache-object-infiniteresourceoptions-ihookdispatcher-funcint-t2)

## `UseInfiniteResource<T1, T2>(RenderContext, Func<T2, CancellationToken, Task<Page<T1, T2>>>, object[], InfiniteResourceOptions, IHookDispatcher, Func<int, T2>)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseInfiniteResourceExtensions.UseInfiniteResource``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``1,System.Threading.CancellationToken,System.Threading.Tasks.Task{Microsoft.UI.Reactor.Core.Page{``0,``1}}},System.Object[],Microsoft.UI.Reactor.Core.InfiniteResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher,System.Func{System.Int32,``1})`

### Summary

Overload that reads the ambient `QueryCache` from
`QueryCache`. `ReactorHost` installs a
process-wide default cache at startup; tests or subtrees may override it via
<c>.Provide(AppContexts.QueryCache, customCache)</c>.

## `UseInfiniteResource<T1, T2>(RenderContext, Func<T2, CancellationToken, Task<Page<T1, T2>>>, QueryCache, object[], InfiniteResourceOptions, IHookDispatcher, Func<int, T2>)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseInfiniteResourceExtensions.UseInfiniteResource``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``1,System.Threading.CancellationToken,System.Threading.Tasks.Task{Microsoft.UI.Reactor.Core.Page{``0,``1}}},Microsoft.UI.Reactor.Core.QueryCache,System.Object[],Microsoft.UI.Reactor.Core.InfiniteResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher,System.Func{System.Int32,``1})`

### Summary

Returns the `InfiniteResource` owned by this hook slot. The
resource's state is driven by `fetchPage`; `deps`
controls cache-keying and deps-change restart.


