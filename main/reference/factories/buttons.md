---
title: "Factories: Buttons"
description: "Buttons, toggle buttons, split buttons and hyperlink buttons."
sidebar:
  label: "Buttons"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Commanding](/microsoft-ui-reactor/main/guides/commanding)

## `Button` [#button]

```csharp
ButtonElement Button(Command command)
ButtonElement Button(Element content, Action? onClick = null)
ButtonElement Button(string label, Action? onClick = null)
```

| Overload | Description |
| --- | --- |
| `Button(Command)` | Creates a Button driven by a Command. Maps Label → Content; the command's Execute/ExecuteAsync is invoked on click and its IsEnabled / Description / Accelerator / AccessKey are applied by the reconciler from the typed [`ButtonElement.Command`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement) property (issues #153, #637) — no per-render Setters array or lambda is allocated, and a bare `new ButtonElement(cmd.Label) { Command = cmd }` record-init behaves identically. |
| `Button(Element, Action?)` |  |
| `Button(string, Action?)` |  |

**Returns:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `onClick` | `Action?` | `null` |  |
| `label` | `string` |  |  |

## `DropDownButton` [#dropdownbutton]

```csharp
DropDownButtonElement DropDownButton(Element content, Element? flyout = null)
DropDownButtonElement DropDownButton(string label, Element? flyout = null)
```

| Overload | Description |
| --- | --- |
| `DropDownButton(Element, Element?)` | Creates a drop-down button with declarative content. |
| `DropDownButton(string, Element?)` |  |

**Returns:** [`DropDownButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#dropdownbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `flyout` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `label` | `string` |  |  |

## `HyperlinkButton` [#hyperlinkbutton]

```csharp
HyperlinkButtonElement HyperlinkButton(Command command)
HyperlinkButtonElement HyperlinkButton(Element content, Uri? navigateUri = null, Action? onClick = null)
HyperlinkButtonElement HyperlinkButton(string content, Uri? navigateUri = null, Action? onClick = null)
```

| Overload | Description |
| --- | --- |
| `HyperlinkButton(Command)` | Creates a HyperlinkButton driven by a Command. Maps Label → Content, Execute → Click. For external navigation, chain [`NavigateUri`](/microsoft-ui-reactor/main/reference/modifiers/buttons#navigateuri): `HyperlinkButton(cmd).NavigateUri(new Uri("https://..."))`. |
| `HyperlinkButton(Element, Uri?, Action?)` | Creates a hyperlink button with declarative content. |
| `HyperlinkButton(string, Uri?, Action?)` |  |

**Returns:** [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) or `string` |  |  |
| `navigateUri` | `Uri?` | `null` |  |
| `onClick` | `Action?` | `null` |  |

## `RepeatButton` [#repeatbutton]

```csharp
RepeatButtonElement RepeatButton(Command command)
RepeatButtonElement RepeatButton(Element content, Action? onClick = null)
RepeatButtonElement RepeatButton(string label, Action? onClick = null)
```

| Overload | Description |
| --- | --- |
| `RepeatButton(Command)` | Creates a RepeatButton driven by a Command. Click auto-repeats while held. |
| `RepeatButton(Element, Action?)` | Creates a repeat button with declarative content. |
| `RepeatButton(string, Action?)` |  |

**Returns:** [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `onClick` | `Action?` | `null` |  |
| `label` | `string` |  |  |

## `SplitButton` [#splitbutton]

```csharp
SplitButtonElement SplitButton(Command command, Element? flyout = null)
SplitButtonElement SplitButton(Element content, Action? onClick = null, Element? flyout = null)
SplitButtonElement SplitButton(string label, Action? onClick = null, Element? flyout = null)
```

| Overload | Description |
| --- | --- |
| `SplitButton(Command, Element?)` | Creates a SplitButton driven by a Command for the primary action. The flyout (dropdown portion) is independent and supplied separately. |
| `SplitButton(Element, Action?, Element?)` | Creates a split button with declarative primary content. |
| `SplitButton(string, Action?, Element?)` |  |

**Returns:** [`SplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#splitbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |  |
| `flyout` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `onClick` | `Action?` | `null` |  |
| `label` | `string` |  |  |

## `ThreeStateToggleButton` [#threestatetogglebutton]

```csharp
ToggleButtonElement ThreeStateToggleButton(Element content, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
ToggleButtonElement ThreeStateToggleButton(string label, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
```

| Overload | Description |
| --- | --- |
| `ThreeStateToggleButton(Element, bool?, Action<bool?>?)` | Creates a three-state toggle button with declarative content. |
| `ThreeStateToggleButton(string, bool?, Action<bool?>?)` | Three-state toggle button (true → false → null → ...). Matches the established `ThreeStateCheckBox` factory pattern from spec 039 §2.4. |

**Returns:** [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `checkedState` | `bool?` | `null` |  |
| `onCheckedStateChanged` | `Action<bool?>?` | `null` |  |
| `label` | `string` |  |  |

## `ToggleButton` [#togglebutton]

```csharp
ToggleButtonElement ToggleButton(Command command, bool isChecked = false)
ToggleButtonElement ToggleButton(Element content, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
ToggleButtonElement ToggleButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
```

| Overload | Description |
| --- | --- |
| `ToggleButton(Command, bool)` | Creates a ToggleButton driven by a Command. The command fires on each toggle (both check and uncheck) — per the spec's "Option A" semantics. Use the `isChecked` parameter to seed the initial state. |
| `ToggleButton(Element, bool, Action<bool>?)` | Creates a toggle button with declarative content. |
| `ToggleButton(string, bool, Action<bool>?)` |  |

**Returns:** [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |  |
| `isChecked` | `bool` | `false` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `label` | `string` |  |  |

## `ToggleSplitButton` [#togglesplitbutton]

```csharp
ToggleSplitButtonElement ToggleSplitButton(Command command, Optional<bool> isChecked = default, Element? flyout = null)
ToggleSplitButtonElement ToggleSplitButton(Element content, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, Element? flyout = null)
ToggleSplitButtonElement ToggleSplitButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, Element? flyout = null)
```

| Overload | Description |
| --- | --- |
| `ToggleSplitButton(Command, Optional<bool>, Element?)` | Creates a ToggleSplitButton driven by a Command (fires on each toggle). |
| `ToggleSplitButton(Element, Optional<bool>, Action<bool>?, Element?)` | Creates a toggle split button with declarative primary content. |
| `ToggleSplitButton(string, Optional<bool>, Action<bool>?, Element?)` | Creates a [`ToggleSplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglesplitbuttonelement). `isChecked` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the WinUI control own its checked state (uncontrolled). Pass `true`/`false` (implicit `T → Optional<T>`) to control it from a parent state. |

**Returns:** [`ToggleSplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglesplitbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |  |
| `isChecked` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `flyout` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `label` | `string` |  |  |
