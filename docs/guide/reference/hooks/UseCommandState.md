# UseCommandState

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCommandState`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Allocates the stable hook shape shared by both [UseCommand](UseCommand.md) ([guide](../../hooks.md)) overloads:
the IsExecuting / IsDebouncing state, the re-entrance guard and debounce-slot refs, and an
unmount effect that disposes any live re-enable timer so it cannot fire
<c>setIsDebouncing</c> / request a re-render against a torn-down context. These hooks are
allocated unconditionally so the hook order is identical regardless of the command's shape.


