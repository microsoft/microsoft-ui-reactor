---
title: "Elements: Buttons"
description: "Buttons, toggle buttons, split buttons and hyperlink buttons."
sidebar:
  label: "Buttons"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Commanding](/microsoft-ui-reactor/main/guides/commanding)

## `ButtonElement` [#buttonelement]

The element record that describes a WinUI [`Button`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.button) control.

```csharp
public record ButtonElement(string Label, Action? OnClick = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Button`](/microsoft-ui-reactor/main/reference/factories/buttons#button)

**Modifiers:** [`AccentButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#accentbutton), [`Click`](/microsoft-ui-reactor/main/reference/modifiers/buttons#click), [`Command`](/microsoft-ui-reactor/main/reference/modifiers/buttons#command), [`DisabledFocusable`](/microsoft-ui-reactor/main/reference/modifiers/buttons#disabledfocusable), [`IsDisabledFocusable`](/microsoft-ui-reactor/main/reference/modifiers/buttons#isdisabledfocusable), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SubtleButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#subtlebutton), [`TextLink`](/microsoft-ui-reactor/main/reference/modifiers/buttons#textlink)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](/microsoft-ui-reactor/main/reference/input/Command) |  | The [`ButtonElement.Command`](#buttonelement) bound to this button via the `Button(Command)` factory, the `.Command()` modifier, or a direct `new ButtonElement(cmd.Label) { Command = cmd }` / `with { Command = cmd }` record-init (issue #153, made fully uniform in issue #637). Carries the command so the reconciler can compare it field-aware in [`Element.ShallowEquals`](/microsoft-ui-reactor/main/reference/elements/Element) and apply its metadata + enabled state through a descriptor entry — no per-render [`ButtonElement.Setters`](#buttonelement) lambda. **Every** binding path is now equivalent: dispatch (Execute/ExecuteAsync) is invoked by the click trampoline when [`ButtonElement.OnClick`](#buttonelement) is null, [`Command.IsEnabled`](/microsoft-ui-reactor/main/reference/input/Command#isenabled) is folded into the IsEnabled descriptor entry (see [`ButtonElement.EffectiveIsEnabled`](#buttonelement)), and the command counts as a callback source for [`ButtonElement.HasCallbacks`](#buttonelement). The `init` accessor is public: a bare record-init invokes the command on click exactly like the factory/modifier. |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `IsDisabledFocusable` | `bool` |  | When true, the button is visually dimmed and its `OnClick` handler is suppressed, but it stays keyboard-focusable and reachable via Tab. Use for submit buttons gated on validation so users can discover them and the disable state doesn't trap keyboard navigation through commit- on-blur inputs. Conceptually equivalent to Fluent UI React's `disabledFocusable` / ARIA's `aria-disabled`. UIA still reports the button as enabled — full assistive-tech "unavailable" reporting requires a custom `ButtonAutomationPeer` and is tracked as a follow-up. |
| `IsEnabled` | `bool` |  |  |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |

## `DropDownButtonElement` [#dropdownbuttonelement]

The element record that describes a WinUI [`DropDownButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.dropdownbutton) control.

```csharp
public record DropDownButtonElement(string Label, Element? Flyout = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`DropDownButton`](/microsoft-ui-reactor/main/reference/factories/buttons#dropdownbutton)

**Modifiers:** [`AccentButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#accentbutton), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SubtleButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#subtlebutton)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `Flyout` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `Label` | `string` |  |  |

## `HyperlinkButtonElement` [#hyperlinkbuttonelement]

The element record that describes a WinUI [`HyperlinkButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.hyperlinkbutton) control.

```csharp
public record HyperlinkButtonElement(string Content, Uri? NavigateUri = null, Action? OnClick = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`HyperlinkButton`](/microsoft-ui-reactor/main/reference/factories/buttons#hyperlinkbutton)

**Modifiers:** [`Click`](/microsoft-ui-reactor/main/reference/modifiers/buttons#click), [`Command`](/microsoft-ui-reactor/main/reference/modifiers/buttons#command), [`NavigateUri`](/microsoft-ui-reactor/main/reference/modifiers/buttons#navigateuri), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TextLink`](/microsoft-ui-reactor/main/reference/modifiers/buttons#textlink)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](/microsoft-ui-reactor/main/reference/input/Command) |  | The command bound via `HyperlinkButton(Command)` / `.Command()` / a direct `new HyperlinkButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths dispatch + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `Content` | `string` |  |  |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `NavigateUri` | `Uri?` | `null` |  |
| `OnClick` | `Action?` | `null` |  |

## `RepeatButtonElement` [#repeatbuttonelement]

The element record that describes a WinUI [`RepeatButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.repeatbutton) control.

```csharp
public record RepeatButtonElement(string Label, Action? OnClick = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RepeatButton`](/microsoft-ui-reactor/main/reference/factories/buttons#repeatbutton)

**Modifiers:** [`Click`](/microsoft-ui-reactor/main/reference/modifiers/buttons#click), [`Command`](/microsoft-ui-reactor/main/reference/modifiers/buttons#command), [`Delay`](/microsoft-ui-reactor/main/reference/modifiers/buttons#delay), [`Interval`](/microsoft-ui-reactor/main/reference/modifiers/buttons#interval), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](/microsoft-ui-reactor/main/reference/input/Command) |  | The command bound via `RepeatButton(Command)` / `.Command()` / a direct `new RepeatButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths dispatch + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `Delay` | `int` |  |  |
| `Interval` | `int` |  |  |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |

## `SplitButtonElement` [#splitbuttonelement]

The element record that describes a WinUI [`SplitButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.splitbutton) control.

```csharp
public record SplitButtonElement(string Label, Action? OnClick = null, Element? Flyout = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`SplitButton`](/microsoft-ui-reactor/main/reference/factories/buttons#splitbutton)

**Modifiers:** [`AccentButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#accentbutton), [`Click`](/microsoft-ui-reactor/main/reference/modifiers/buttons#click), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SubtleButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#subtlebutton)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](/microsoft-ui-reactor/main/reference/input/Command) |  | The command bound via `SplitButton(Command)` / a direct `new SplitButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths dispatch the primary action + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `Flyout` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |

## `ToggleButtonElement` [#togglebuttonelement]

The element record that describes a WinUI [`ToggleButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.togglebutton) control.

```csharp
public record ToggleButtonElement(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ThreeStateToggleButton`](/microsoft-ui-reactor/main/reference/factories/buttons#threestatetogglebutton), [`ToggleButton`](/microsoft-ui-reactor/main/reference/factories/buttons#togglebutton)

**Modifiers:** [`CheckedState`](/microsoft-ui-reactor/main/reference/modifiers/buttons#checkedstate), [`CheckedStateChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#checkedstatechanged), [`Command`](/microsoft-ui-reactor/main/reference/modifiers/buttons#command), [`IsCheckedChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#ischeckedchanged), [`IsThreeState`](/microsoft-ui-reactor/main/reference/modifiers/buttons#isthreestate), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `CheckedState` | `bool?` |  | Three-state value (`null` = indeterminate). Active only when [`ToggleButtonElement.IsThreeState`](#togglebuttonelement) is true. |
| `Command` | [`Command?`](/microsoft-ui-reactor/main/reference/input/Command) |  | The command bound via `ToggleButton(Command)` / `.Command()` / a direct `new ToggleButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths fire the command on each toggle + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `IsChecked` | `bool` | `false` |  |
| `IsThreeState` | `bool` |  | Enable the three-state cycle (true → false → null → true). Pair with [`ToggleButtonElement.CheckedState`](#togglebuttonelement) and [`ToggleButtonElement.OnCheckedStateChanged`](#togglebuttonelement); the non-nullable [`ToggleButtonElement.IsChecked`](#togglebuttonelement) primary is ignored in this mode. Mirrors the established `CheckBoxElement` precedent. |
| `Label` | `string` |  |  |
| `OnCheckedStateChanged` | `Action<bool?>?` |  | Three-state change handler. Receives `null` for indeterminate. |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |

## `ToggleSplitButtonElement` [#togglesplitbuttonelement]

The element record that describes a WinUI [`ToggleSplitButton`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.togglesplitbutton) control.

```csharp
public record ToggleSplitButtonElement(string Label, Optional<bool> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, Element? Flyout = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ToggleSplitButton`](/microsoft-ui-reactor/main/reference/factories/buttons#togglesplitbutton)

**Modifiers:** [`AccentButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#accentbutton), [`IsCheckedChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#ischeckedchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SubtleButton`](/microsoft-ui-reactor/main/reference/modifiers/buttons#subtlebutton)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Command` | [`Command?`](/microsoft-ui-reactor/main/reference/input/Command) |  | The command bound via `ToggleSplitButton(Command)` / a direct `new ToggleSplitButtonElement(cmd.Label) { Command = cmd }` record-init (issue #153; made uniform in issue #637). All paths fire the command on each toggle + apply IsEnabled identically. See [`ButtonElement.Command`](#buttonelement). |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `Flyout` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `IsChecked` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `Label` | `string` |  |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |
