# Command\<T\>

Immutable (init-only) parameterized command descriptor. The action receives an argument of type `T`, enabling a single command definition to operate on different targets: var delete = new Command\<Item\> \{ Label = "Delete", Execute = item =\> Remove(item) \}; MenuItem(delete, selectedItem)

```csharp
public sealed record Command<T>
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

Leading-edge debounce window, in milliseconds. `0` (the default) disables debouncing. When \> 0 and the command is processed through [`UseCommand`](../hooks/UseCommand.md#usecommandtcommandt), the first fire is accepted and any subsequent fire within the window is dropped, with [`Command.IsDebouncing`](#isdebouncing) (and therefore [`Command.IsEnabled`](#isenabled)=false) reflecting the window so the bound control disables. **Like [`Command.DebounceMs`](Command.md#debouncems), this only works through [`UseCommand`](../hooks/UseCommand.md#usecommandtcommandt)** — a raw `new Command<T> { DebounceMs = … }` bound directly is inert and does not debounce. See [`Command.DebounceMs`](Command.md#debouncems) for the full contract (issue #136).

```csharp
public int DebounceMs  // get, init
```

### `Description`

Tooltip / accessibility description.

```csharp
public string? Description  // get, init
```

### `Execute`

Synchronous action that receives a parameter.

```csharp
public Action<T>? Execute  // get, init
```

### `ExecuteAsync`

Asynchronous action that receives a parameter. Use with [`UseCommand`](../hooks/UseCommand.md#usecommandtcommandt) for lifecycle tracking.

```csharp
public Func<T, Task>? ExecuteAsync  // get, init
```

### `Icon`

Icon to display alongside the command.

```csharp
public IconData? Icon  // get, init
```

### `IsDebouncing`

Whether the command is currently inside its leading-edge debounce window. Framework-managed state set by [`UseCommand`](../hooks/UseCommand.md#usecommandtcommandt) when [`Command.DebounceMs`](#debouncems) \> 0; read-only to app authors (internal init).

```csharp
public bool IsDebouncing  // get
```

### `IsEnabled`

Computed: the command is enabled only when it can execute, is not currently executing, and is not inside its leading-edge debounce window.

```csharp
public bool IsEnabled  // get
```

### `IsExecuting`

Whether the command is currently executing an async operation.

```csharp
public bool IsExecuting  // get, init
```

### `Label`

Human-readable label shown on buttons, menu items, tooltips, etc.

```csharp
public required string Label  // get, init
```
