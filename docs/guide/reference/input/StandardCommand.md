# StandardCommand

Factory methods for the 16 standard application commands with pre-filled metadata (label, icon, keyboard accelerator). Each command has sync and async overloads: var cut = StandardCommand.Cut(() =\> CutSelection()); var save = StandardCommand.Save(async () =\> await SaveAsync()); Labels use plain English by default. Override with `with` for localization: var cut = StandardCommand.Cut(action) with \{ Label = intl.Message(keys.Cut) \};

```csharp
public static class StandardCommand
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Commanding](../../commanding.md)

## Methods

### `Backward`

```csharp
public static Command Backward(Action execute, bool canExecute = true)
public static Command Backward(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Close`

```csharp
public static Command Close(Action execute, bool canExecute = true)
public static Command Close(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Copy`

```csharp
public static Command Copy(Action execute, bool canExecute = true)
public static Command Copy(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Cut`

```csharp
public static Command Cut(Action execute, bool canExecute = true)
public static Command Cut(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Delete`

```csharp
public static Command Delete(Action execute, bool canExecute = true)
public static Command Delete(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Forward`

```csharp
public static Command Forward(Action execute, bool canExecute = true)
public static Command Forward(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Open`

```csharp
public static Command Open(Action execute, bool canExecute = true)
public static Command Open(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Paste`

```csharp
public static Command Paste(Action execute, bool canExecute = true)
public static Command Paste(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Pause`

```csharp
public static Command Pause(Action execute, bool canExecute = true)
public static Command Pause(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Play`

```csharp
public static Command Play(Action execute, bool canExecute = true)
public static Command Play(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Redo`

```csharp
public static Command Redo(Action execute, bool canExecute = true)
public static Command Redo(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Save`

```csharp
public static Command Save(Action execute, bool canExecute = true)
public static Command Save(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `SelectAll`

```csharp
public static Command SelectAll(Action execute, bool canExecute = true)
public static Command SelectAll(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Share`

```csharp
public static Command Share(Action execute, bool canExecute = true)
public static Command Share(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Stop`

```csharp
public static Command Stop(Action execute, bool canExecute = true)
public static Command Stop(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |

### `Undo`

```csharp
public static Command Undo(Action execute, bool canExecute = true)
public static Command Undo(Func<Task> executeAsync, bool canExecute = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `execute` | `Action` |  |  |
| `canExecute` | `bool` | `true` |  |
| `executeAsync` | `Func<Task>` |  |  |
