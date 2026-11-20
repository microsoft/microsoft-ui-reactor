# Factories: Buttons

Buttons, toggle buttons, split buttons and hyperlink buttons.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Controls](../../controls.md), [Commanding](../../commanding.md)

## `Button`

Creates a Button driven by a Command. Maps Label → Content; the command's Execute/ExecuteAsync is invoked on click and its IsEnabled / Description / Accelerator / AccessKey are applied by the reconciler from the typed [`ButtonElement.Command`](../elements/buttons.md#buttonelement) property (issues #153, #637) — no per-render Setters array or lambda is allocated, and a bare `new ButtonElement(cmd.Label) { Command = cmd }` record-init behaves identically.

```csharp
ButtonElement Button(Command command)
ButtonElement Button(Element content, Action? onClick = null)
ButtonElement Button(string label, Action? onClick = null)
```

**Returns:** [`ButtonElement`](../elements/buttons.md#buttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `onClick` | `Action?` | `null` |  |
| `label` | `string` |  |  |

## `DropDownButton`

Creates a drop-down button with declarative content.

```csharp
DropDownButtonElement DropDownButton(Element content, Element? flyout = null)
DropDownButtonElement DropDownButton(string label, Element? flyout = null)
```

**Returns:** [`DropDownButtonElement`](../elements/buttons.md#dropdownbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `flyout` | [`Element?`](../elements/Element.md) | `null` |  |
| `label` | `string` |  |  |

## `HyperlinkButton`

Creates a HyperlinkButton driven by a Command. Maps Label → Content, Execute → Click. For external navigation, chain [`NavigateUri`](../modifiers/buttons.md#navigateuri): `HyperlinkButton(cmd).NavigateUri(new Uri("https://..."))`.

```csharp
HyperlinkButtonElement HyperlinkButton(Command command)
HyperlinkButtonElement HyperlinkButton(Element content, Uri? navigateUri = null, Action? onClick = null)
HyperlinkButtonElement HyperlinkButton(string content, Uri? navigateUri = null, Action? onClick = null)
```

| Overload | Description |
| --- | --- |
| `HyperlinkButton(Command)` | Creates a HyperlinkButton driven by a Command. Maps Label → Content, Execute → Click. For external navigation, chain [`NavigateUri`](../modifiers/buttons.md#navigateuri): `HyperlinkButton(cmd).NavigateUri(new Uri("https://..."))`. |
| `HyperlinkButton(Element, Uri?, Action?)` | Creates a hyperlink button with declarative content. |
| `HyperlinkButton(string, Uri?, Action?)` |  |

**Returns:** [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `content` | [`Element`](../elements/Element.md) or `string` |  |  |
| `navigateUri` | `Uri?` | `null` |  |
| `onClick` | `Action?` | `null` |  |

## `RepeatButton`

Creates a RepeatButton driven by a Command. Click auto-repeats while held.

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

**Returns:** [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `onClick` | `Action?` | `null` |  |
| `label` | `string` |  |  |

## `SplitButton`

Creates a SplitButton driven by a Command for the primary action. The flyout (dropdown portion) is independent and supplied separately.

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

**Returns:** [`SplitButtonElement`](../elements/buttons.md#splitbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `flyout` | [`Element?`](../elements/Element.md) | `null` |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `onClick` | `Action?` | `null` |  |
| `label` | `string` |  |  |

## `ThreeStateToggleButton`

Creates a three-state toggle button with declarative content.

```csharp
ToggleButtonElement ThreeStateToggleButton(Element content, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
ToggleButtonElement ThreeStateToggleButton(string label, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
```

| Overload | Description |
| --- | --- |
| `ThreeStateToggleButton(Element, bool?, Action<bool?>?)` | Creates a three-state toggle button with declarative content. |
| `ThreeStateToggleButton(string, bool?, Action<bool?>?)` | Three-state toggle button (true → false → null → ...). Matches the established `ThreeStateCheckBox` factory pattern from spec 039 §2.4. |

**Returns:** [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `checkedState` | `bool?` | `null` |  |
| `onCheckedStateChanged` | `Action<bool?>?` | `null` |  |
| `label` | `string` |  |  |

## `ToggleButton`

Creates a ToggleButton driven by a Command. The command fires on each toggle (both check and uncheck) — per the spec's "Option A" semantics. Use the `isChecked` parameter to seed the initial state.

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

**Returns:** [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `isChecked` | `bool` | `false` |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `label` | `string` |  |  |

## `ToggleSplitButton`

Creates a ToggleSplitButton driven by a Command (fires on each toggle).

```csharp
ToggleSplitButtonElement ToggleSplitButton(Command command, Optional<bool> isChecked = default, Element? flyout = null)
ToggleSplitButtonElement ToggleSplitButton(Element content, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, Element? flyout = null)
ToggleSplitButtonElement ToggleSplitButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, Element? flyout = null)
```

| Overload | Description |
| --- | --- |
| `ToggleSplitButton(Command, Optional<bool>, Element?)` | Creates a ToggleSplitButton driven by a Command (fires on each toggle). |
| `ToggleSplitButton(Element, Optional<bool>, Action<bool>?, Element?)` | Creates a toggle split button with declarative primary content. |
| `ToggleSplitButton(string, Optional<bool>, Action<bool>?, Element?)` | Creates a [`ToggleSplitButtonElement`](../elements/buttons.md#togglesplitbuttonelement). `isChecked` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the WinUI control own its checked state (uncontrolled). Pass `true`/`false` (implicit `T → Optional<T>`) to control it from a parent state. |

**Returns:** [`ToggleSplitButtonElement`](../elements/buttons.md#togglesplitbuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `isChecked` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `flyout` | [`Element?`](../elements/Element.md) | `null` |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `label` | `string` |  |  |
