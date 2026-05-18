# UseContext

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseContext``1(Microsoft.UI.Reactor.Core.Context{``0})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Reads the nearest ancestor's provided value for the given context.
Returns the context's DefaultValue if no provider exists in the ancestor chain.
Follows hook rules — must be called in the same order every render.

## Featured in

- [Hooks](../../hooks.md)

