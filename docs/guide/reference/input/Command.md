# Command

Immutable (init-only) command descriptor that bundles an action with its metadata (label, icon, keyboard accelerator, enabled state). Define once, use in any surface: var save = new Command \{ Label = "Save", Execute = () =\> Save(), Icon = SymbolIcon("Save") \}; AppBarButton(save) // toolbar MenuItem(save) // menu Button(save) // inline

```csharp
public sealed record Command
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Commanding](../../commanding.md)

## Constructors

### `Command`

```csharp
public Command()
```

## Properties

### `Accelerator`

Keyboard shortcut for this command.

```csharp
public KeyboardAcceleratorData? Accelerator  // get, init
```

### `AccessKey`

Access key (Alt+key) for this command.

```csharp
public string? AccessKey  // get, init
```

### `CanExecute`

Whether the command's action can be invoked. Defaults to true.

```csharp
public bool CanExecute  // get, init
```

### `DebounceMs`

Leading-edge debounce window, in milliseconds. `0` (the default) disables debouncing and preserves the un-debounced behavior exactly.

When \> 0 and the command is processed through [`UseCommand`](../hooks/UseCommand.md#usecommandcommand), the first fire is accepted and any subsequent fire within `DebounceMs` of it is dropped (a no-op). For the duration of the window [`Command.IsDebouncing`](#isdebouncing) is true, so [`Command.IsEnabled`](#isenabled) reports false and the bound control visibly disables, then re-enables when the window elapses. This is the framework-owned replacement for the "wrap a sync action in `Task.Delay` to absorb double-clicks" pattern (issue #136).

For async commands, [`Command.IsExecuting`](#isexecuting) already tracks the lambda's lifetime; `DebounceMs` is the time-based generalization that keeps the disabled window open past the lambda's return (the effective disabled window is the longer of the two).

**DebounceMs only works through [`UseCommand`](../hooks/UseCommand.md#usecommandcommand).** The debounce state (last-fire window + re-enable timer) lives in the `UseCommand` hook's backing store, which is the only place that persists across renders (the [`Command`](Command.md) record itself is reconstructed every render). A raw `new Command { DebounceMs = … }` bound directly to a control — without being passed through `UseCommand` — has nowhere to persist that state and is therefore **inert: it does NOT debounce**. Always wrap a debounced command with `UseCommand`:

```csharp
var run = UseCommand(new Command { Label = "Run", Execute = Run, DebounceMs = 1500 });
```

```csharp
public int DebounceMs  // get, init
```

### `Description`

Tooltip / accessibility description.

```csharp
public string? Description  // get, init
```

### `Execute`

Synchronous action. Mutually exclusive with [`Command.ExecuteAsync`](#executeasync).

```csharp
public Action? Execute  // get, init
```

### `ExecuteAsync`

Asynchronous action. Use with [`UseCommand`](../hooks/UseCommand.md#usecommandcommand) to get automatic IsExecuting tracking and re-entrance guards.

```csharp
public Func<Task>? ExecuteAsync  // get, init
```

### `Icon`

Icon to display alongside the command.

```csharp
public IconData? Icon  // get, init
```

### `IsDebouncing`

Whether the command is currently inside its leading-edge debounce window. This is framework-managed state set by [`UseCommand`](../hooks/UseCommand.md#usecommandcommand) when [`Command.DebounceMs`](#debouncems) \> 0; it is read-only to app authors (internal init).

```csharp
public bool IsDebouncing  // get
```

### `IsEnabled`

Computed: the command is enabled only when it can execute, is not currently executing, and is not inside its leading-edge debounce window.

```csharp
public bool IsEnabled  // get
```

### `IsExecuting`

Whether the command is currently executing an async operation. Managed by [`UseCommand`](../hooks/UseCommand.md#usecommandcommand).

```csharp
public bool IsExecuting  // get, init
```

### `Label`

Human-readable label shown on buttons, menu items, tooltips, etc.

```csharp
public required string Label  // get, init
```
