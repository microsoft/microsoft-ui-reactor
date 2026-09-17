# UseNavigationLifecycle

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseNavigationLifecycle(System.Action{Microsoft.UI.Reactor.Navigation.NavigatingToContext},System.Action{Microsoft.UI.Reactor.Navigation.NavigatedToContext},System.Action{Microsoft.UI.Reactor.Navigation.NavigatingFromContext},System.Action{Microsoft.UI.Reactor.Navigation.NavigatedFromContext})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Registers lifecycle callbacks that fire during navigation events.

- `onNavigatedTo` — fires after this page becomes active.
- `onNavigatingFrom` — fires before navigating away. Call `ctx.Cancel()` to block.
- `onNavigatedFrom` — fires after this page is no longer active.

Callbacks are always updated to the latest references on every render.


