# UseCommand

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCommand(Microsoft.UI.Reactor.Core.Command)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseCommand(Command)`](#usecommandcommand)
- [`UseCommand<T1>(Command<T1>)`](#usecommandt1commandt1)

## `UseCommand(Command)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCommand(Microsoft.UI.Reactor.Core.Command)`

### Summary

Processes a Command for use in a component. Always consumes a **stable hook shape**
(independent of whether the command is sync/async or debounced), so a command at a given
call site can flip between sync↔async or `Command.DebounceMs` 0↔N across
renders without ever reordering hook slots. For a pure sync command with no
`Command.DebounceMs` the original command is returned unchanged (identity
preserved). For async commands, wraps ExecuteAsync with automatic IsExecuting tracking and
re-entrance guards. When `Command.DebounceMs` > 0, wraps the dispatch with a
leading-edge debounce: a fire within the window of the prior accepted fire is dropped and
`Command.IsDebouncing` (hence `Command.IsEnabled`=false) reflects
the window so the bound control disables, re-enabling when the window elapses. The returned
command has a sync Execute action, ExecuteAsync = null, and preserves the authored
DebounceMs value (re-passing it through UseCommand is a no-op — debounce is never applied
twice).

## `UseCommand<T1>(Command<T1>)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCommand``1(Microsoft.UI.Reactor.Core.Command{``0})`

### Summary

Processes a parameterized Command for use in a component. Consumes the same
**stable hook shape** as the non-generic [UseCommand](UseCommand.md#usecommandcommand) and applies
the same async tracking and leading-edge `Command.DebounceMs` debounce.


