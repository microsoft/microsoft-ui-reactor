# UseCommand

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCommand(Microsoft.UI.Reactor.Core.Command)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Processes a Command for use in a component. Always consumes a stable hook shape
(independent of whether the command is sync/async or debounced), so a command at a
given call site can flip between sync↔async or DebounceMs 0↔N across renders without
reordering hook slots. For a pure sync command with no DebounceMs the original command
is returned unchanged (identity preserved). For async commands, wraps ExecuteAsync with
automatic IsExecuting tracking and re-entrance guards. When DebounceMs > 0, wraps the
dispatch with a leading-edge debounce so a fire within the window is dropped and
IsDebouncing (hence IsEnabled = false) disables the bound control until the window
elapses. The returned command has a sync Execute action, ExecuteAsync = null, and
preserves the authored DebounceMs value (re-passing it through UseCommand is a no-op —
debounce is never applied twice).


