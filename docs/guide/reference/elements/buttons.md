# Elements: Buttons

Buttons, toggle buttons, split buttons and hyperlink buttons.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Controls](../../controls.md), [Commanding](../../commanding.md)

## `ButtonElement`

The element record that describes a WinUI [`Button`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.button) control.

```csharp
public record ButtonElement(string Label, Action? OnClick = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Button`](../factories/buttons.md#button)

**Modifiers:** [`AccentButton`](../modifiers/buttons.md#accentbutton), [`Click`](../modifiers/buttons.md#click), [`Command`](../modifiers/buttons.md#command), [`DisabledFocusable`](../modifiers/buttons.md#disabledfocusable), [`IsDisabledFocusable`](../modifiers/buttons.md#isdisabledfocusable), [`Set`](../modifiers/native-properties.md#set), [`SubtleButton`](../modifiers/buttons.md#subtlebutton), [`TextLink`](../modifiers/buttons.md#textlink)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](../input/Command.md) |  | The [`ButtonElement.Command`](#buttonelement) bound to this button via the `Button(Command)` factory, the `.Command()` modifier, or a direct `new ButtonElement(cmd.Label) { Command = cmd }` / `with { Command = cmd }` record-init (issue #153, made fully uniform in issue #637). Carries the command so the reconciler can compare it field-aware in [`Element.ShallowEquals`](Element.md) and apply its metadata + enabled state through a descriptor entry — no per-render [`ButtonElement.Setters`](#buttonelement) lambda. **Every** binding path is now equivalent: dispatch (Execute/ExecuteAsync) is invoked by the click trampoline when [`ButtonElement.OnClick`](#buttonelement) is null, [`Command.IsEnabled`](../input/Command.md#isenabled) is folded into the IsEnabled descriptor entry (see [`ButtonElement.EffectiveIsEnabled`](#buttonelement)), and the command counts as a callback source for [`ButtonElement.HasCallbacks`](#buttonelement). The `init` accessor is public: a bare record-init invokes the command on click exactly like the factory/modifier. |
| `ContentElement` | [`Element?`](Element.md) |  |  |
| `IsDisabledFocusable` | `bool` |  | When true, the button is visually dimmed and its `OnClick` handler is suppressed, but it stays keyboard-focusable and reachable via Tab. Use for submit buttons gated on validation so users can discover them and the disable state doesn't trap keyboard navigation through commit- on-blur inputs. Conceptually equivalent to Fluent UI React's `disabledFocusable` / ARIA's `aria-disabled`. UIA still reports the button as enabled — full assistive-tech "unavailable" reporting requires a custom `ButtonAutomationPeer` and is tracked as a follow-up. |
| `IsEnabled` | `bool` |  |  |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |

## `DropDownButtonElement`

The element record that describes a WinUI [`DropDownButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.dropdownbutton) control.

```csharp
public record DropDownButtonElement(string Label, Element? Flyout = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`DropDownButton`](../factories/buttons.md#dropdownbutton)

**Modifiers:** [`AccentButton`](../modifiers/buttons.md#accentbutton), [`Set`](../modifiers/native-properties.md#set), [`SubtleButton`](../modifiers/buttons.md#subtlebutton)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `Flyout` | [`Element?`](Element.md) | `null` |  |
| `Label` | `string` |  |  |

## `HyperlinkButtonElement`

The element record that describes a WinUI [`HyperlinkButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.hyperlinkbutton) control.

```csharp
public record HyperlinkButtonElement(string Content, Uri? NavigateUri = null, Action? OnClick = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`HyperlinkButton`](../factories/buttons.md#hyperlinkbutton)

**Modifiers:** [`Click`](../modifiers/buttons.md#click), [`Command`](../modifiers/buttons.md#command), [`NavigateUri`](../modifiers/buttons.md#navigateuri), [`Set`](../modifiers/native-properties.md#set), [`TextLink`](../modifiers/buttons.md#textlink)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](../input/Command.md) |  | The command bound via `HyperlinkButton(Command)` / `.Command()` / a direct `new HyperlinkButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths dispatch + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `Content` | `string` |  |  |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `NavigateUri` | `Uri?` | `null` |  |
| `OnClick` | `Action?` | `null` |  |

## `RepeatButtonElement`

The element record that describes a WinUI [`RepeatButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.repeatbutton) control.

```csharp
public record RepeatButtonElement(string Label, Action? OnClick = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RepeatButton`](../factories/buttons.md#repeatbutton)

**Modifiers:** [`Click`](../modifiers/buttons.md#click), [`Command`](../modifiers/buttons.md#command), [`Delay`](../modifiers/buttons.md#delay), [`Interval`](../modifiers/buttons.md#interval), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](../input/Command.md) |  | The command bound via `RepeatButton(Command)` / `.Command()` / a direct `new RepeatButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths dispatch + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `Delay` | `int` |  |  |
| `Interval` | `int` |  |  |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |

## `SplitButtonElement`

The element record that describes a WinUI [`SplitButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.splitbutton) control.

```csharp
public record SplitButtonElement(string Label, Action? OnClick = null, Element? Flyout = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`SplitButton`](../factories/buttons.md#splitbutton)

**Modifiers:** [`AccentButton`](../modifiers/buttons.md#accentbutton), [`Click`](../modifiers/buttons.md#click), [`Set`](../modifiers/native-properties.md#set), [`SubtleButton`](../modifiers/buttons.md#subtlebutton)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](../input/Command.md) |  | The command bound via `SplitButton(Command)` / a direct `new SplitButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths dispatch the primary action + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `Flyout` | [`Element?`](Element.md) | `null` |  |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |

## `ToggleButtonElement`

The element record that describes a WinUI [`ToggleButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.togglebutton) control.

```csharp
public record ToggleButtonElement(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ThreeStateToggleButton`](../factories/buttons.md#threestatetogglebutton), [`ToggleButton`](../factories/buttons.md#togglebutton)

**Modifiers:** [`CheckedState`](../modifiers/buttons.md#checkedstate), [`CheckedStateChanged`](../modifiers/shared-controls.md#checkedstatechanged), [`Command`](../modifiers/buttons.md#command), [`IsCheckedChanged`](../modifiers/shared-controls.md#ischeckedchanged), [`IsThreeState`](../modifiers/buttons.md#isthreestate), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `CheckedState` | `bool?` |  | Three-state value (`null` = indeterminate). Active only when [`ToggleButtonElement.IsThreeState`](#togglebuttonelement) is true. |
| `Command` | [`Command?`](../input/Command.md) |  | The command bound via `ToggleButton(Command)` / `.Command()` / a direct `new ToggleButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths fire the command on each toggle + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `IsChecked` | `bool` | `false` |  |
| `IsThreeState` | `bool` |  | Enable the three-state cycle (true → false → null → true). Pair with [`ToggleButtonElement.CheckedState`](#togglebuttonelement) and [`ToggleButtonElement.OnCheckedStateChanged`](#togglebuttonelement); the non-nullable [`ToggleButtonElement.IsChecked`](#togglebuttonelement) primary is ignored in this mode. Mirrors the established `CheckBoxElement` precedent. |
| `Label` | `string` |  |  |
| `OnCheckedStateChanged` | `Action<bool?>?` |  | Three-state change handler. Receives `null` for indeterminate. |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |

## `ToggleSplitButtonElement`

The element record that describes a WinUI [`ToggleSplitButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.togglesplitbutton) control.

```csharp
public record ToggleSplitButtonElement(string Label, Optional<bool> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, Element? Flyout = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ToggleSplitButton`](../factories/buttons.md#togglesplitbutton)

**Modifiers:** [`AccentButton`](../modifiers/buttons.md#accentbutton), [`IsCheckedChanged`](../modifiers/shared-controls.md#ischeckedchanged), [`Set`](../modifiers/native-properties.md#set), [`SubtleButton`](../modifiers/buttons.md#subtlebutton)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](../input/Command.md) |  | The command bound via `ToggleSplitButton(Command)` / a direct `new ToggleSplitButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths fire the command on each toggle + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `Flyout` | [`Element?`](Element.md) | `null` |  |
| `IsChecked` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `Label` | `string` |  |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |
