# UseInfiniteResource

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseInfiniteResourceExtensions.UseInfiniteResource``2(Microsoft.UI.Reactor.Core.RenderContext,System.Func{``1,System.Threading.CancellationToken,System.Threading.Tasks.Task{Microsoft.UI.Reactor.Core.Page{``0,``1}}},Microsoft.UI.Reactor.Core.QueryCache,System.Object[],Microsoft.UI.Reactor.Core.InfiniteResourceOptions,Microsoft.UI.Reactor.Hooks.IHookDispatcher,System.Func{System.Int32,``1})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns the `InfiniteResource`1` owned by this hook slot. The
resource's state is driven by <paramref name="fetchPage" />; <paramref name="deps" />
controls cache-keying and deps-change restart.


