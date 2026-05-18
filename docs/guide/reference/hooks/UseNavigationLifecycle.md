# UseNavigationLifecycle

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseNavigationLifecycle(System.Action{Microsoft.UI.Reactor.Navigation.NavigatingToContext},System.Action{Microsoft.UI.Reactor.Navigation.NavigatedToContext},System.Action{Microsoft.UI.Reactor.Navigation.NavigatingFromContext},System.Action{Microsoft.UI.Reactor.Navigation.NavigatedFromContext})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Registers lifecycle callbacks that fire during navigation events.
<list type="bullet"><item><c>onNavigatedTo</c> — fires after this page becomes active.</item><item><c>onNavigatingFrom</c> — fires before navigating away. Call <c>ctx.Cancel()</c> to block.</item><item><c>onNavigatedFrom</c> — fires after this page is no longer active.</item></list>
Callbacks are always updated to the latest references on every render.


