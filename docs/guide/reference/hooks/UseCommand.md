# UseCommand

Processes a Command for use in a component. Always consumes a **stable hook shape** (independent of whether the command is sync/async or debounced), so a command at a given call site can flip between sync↔async or [`Command.DebounceMs`](../input/Command.md#debouncems) 0↔N across renders without ever reordering hook slots. For a pure sync command with no [`Command.DebounceMs`](../input/Command.md#debouncems) the original command is returned unchanged (identity preserved). For async commands, wraps ExecuteAsync with automatic IsExecuting tracking and re-entrance guards. When [`Command.DebounceMs`](../input/Command.md#debouncems) \> 0, wraps the dispatch with a leading-edge debounce: a fire within the window of the prior accepted fire is dropped and [`Command.IsDebouncing`](../input/Command.md#isdebouncing) (hence [`Command.IsEnabled`](../input/Command.md#isenabled)=false) reflects the window so the bound control disables, re-enabling when the window elapses. The returned command has a sync Execute action, ExecuteAsync = null, and preserves the authored DebounceMs value (re-passing it through UseCommand is a no-op — debounce is never applied twice).

```csharp
public Command UseCommand(Command command)
public Command<T> UseCommand<T>(Command<T> command)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Commanding](../../commanding.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseCommand(Command)`](#usecommandcommand) | Processes a Command for use in a component. |
| [`UseCommand<T>(Command<T>)`](#usecommandtcommandt) | Processes a parameterized Command for use in a component. |

### `UseCommand(Command)`

Processes a Command for use in a component. Always consumes a **stable hook shape** (independent of whether the command is sync/async or debounced), so a command at a given call site can flip between sync↔async or [`Command.DebounceMs`](../input/Command.md#debouncems) 0↔N across renders without ever reordering hook slots. For a pure sync command with no [`Command.DebounceMs`](../input/Command.md#debouncems) the original command is returned unchanged (identity preserved). For async commands, wraps ExecuteAsync with automatic IsExecuting tracking and re-entrance guards. When [`Command.DebounceMs`](../input/Command.md#debouncems) \> 0, wraps the dispatch with a leading-edge debounce: a fire within the window of the prior accepted fire is dropped and [`Command.IsDebouncing`](../input/Command.md#isdebouncing) (hence [`Command.IsEnabled`](../input/Command.md#isenabled)=false) reflects the window so the bound control disables, re-enabling when the window elapses. The returned command has a sync Execute action, ExecuteAsync = null, and preserves the authored DebounceMs value (re-passing it through UseCommand is a no-op — debounce is never applied twice).

```csharp
public Command UseCommand(Command command)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |

### `UseCommand<T>(Command<T>)`

Processes a parameterized Command for use in a component. Consumes the same **stable hook shape** as the non-generic [`UseCommand`](#usecommandcommand) and applies the same async tracking and leading-edge [`Command.DebounceMs`](../input/Command-1.md#debouncems) debounce.

```csharp
public Command<T> UseCommand<T>(Command<T> command)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `command` | [`Command<T>`](../input/Command-1.md) |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected Command UseCommand(Command command)
protected Command<T> UseCommand<T>(Command<T> command)
```
