# UseCommand

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCommand(Microsoft.UI.Reactor.Core.Command)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Processes a Command for use in a component. For sync-only commands, returns
the command unchanged (no hook slots consumed). For async commands, wraps ExecuteAsync
with automatic IsExecuting tracking and re-entrance guards. The returned command
always has a sync Execute action and ExecuteAsync = null.


