---
title: "CommandInterop"
description: "Bridges ICommand (MVVM/CommunityToolkit) to Command, enabling migration from ViewModel-based patterns to Reactor's declarative commanding. var reactorCmd = CommandInterop.FromCommand(viewModel.SaveCommand, \"Save\", icon: new SymbolIconData(\"Save\"), accelerator: Accelerator(VirtualKey.S, Control));"
---

```csharp
public static class CommandInterop
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Commanding](/microsoft-ui-reactor/main/guides/commanding)

## Methods [#methods]

### `FromCommand` [#fromcommand]

#### `FromCommand<T>(ICommand, string, IconData?, string?, KeyboardAcceleratorData?)` [#fromcommandticommand-string-icondata-string-keyboardacceleratordata]

Creates a parameterized Command from an ICommand. The ICommand receives the Command's parameter when Execute is called. CanExecute defaults to true because Command\<T\>.CanExecute is a static bool and the parameter is not known at creation time. To evaluate CanExecute dynamically, call this within a component's Render method with a known parameter and pass command.CanExecute(parameter) explicitly.

```csharp
public static Command<T> FromCommand<T>(ICommand command, string label, IconData? icon = null, string? description = null, KeyboardAcceleratorData? accelerator = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | `ICommand` |  |  |
| `label` | `string` |  |  |
| `icon` | [`IconData?`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) | `null` |  |
| `description` | `string?` | `null` |  |
| `accelerator` | [`KeyboardAcceleratorData?`](/microsoft-ui-reactor/main/reference/controls/menu-items#keyboardacceleratordata) | `null` |  |

#### `FromCommand(ICommand, string, IconData?, string?, KeyboardAcceleratorData?, object?)` [#fromcommandicommand-string-icondata-string-keyboardacceleratordata-object]

Creates a Command from an ICommand. CanExecute is evaluated at creation time. For CanExecute to update on each render, call this within a component's Render method.

```csharp
public static Command FromCommand(ICommand command, string label, IconData? icon = null, string? description = null, KeyboardAcceleratorData? accelerator = null, object? parameter = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | `ICommand` |  |  |
| `label` | `string` |  |  |
| `icon` | [`IconData?`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) | `null` |  |
| `description` | `string?` | `null` |  |
| `accelerator` | [`KeyboardAcceleratorData?`](/microsoft-ui-reactor/main/reference/controls/menu-items#keyboardacceleratordata) | `null` |  |
| `parameter` | `object?` | `null` |  |
