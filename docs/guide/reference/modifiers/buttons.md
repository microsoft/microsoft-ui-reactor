# Control modifiers: Buttons

Modifiers that only the controls in the Buttons area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Controls](../../controls.md), [Commanding](../../commanding.md)

## `AccentButton`

Applies the WinUI `AccentButtonStyle` — the accent-color primary-button look. The Style is resolved from `Application.Current.Resources` once; its colors still follow light/dark/contrast switches because the Style's own setters use `{ThemeResource}` values, which WinUI re-resolves on theme change.

**Applied at mount only.** Like [`ApplyStyle`](appearance.md#applystyle) this runs through `OnMount`, which the reconciler gates on first mount — so conditionally chaining it (e.g. `isPrimary? btn.AccentButton(): btn`) will *not* apply or remove the style when the condition flips on an in-place update. Give the two variants different [`WithKey`](lifecycle.md#withkey) values to force a remount when the style must change.

```csharp
ButtonElement AccentButton(this ButtonElement el)
DropDownButtonElement AccentButton(this DropDownButtonElement el)
SplitButtonElement AccentButton(this SplitButtonElement el)
ToggleSplitButtonElement AccentButton(this ToggleSplitButtonElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement), [`DropDownButtonElement`](../elements/buttons.md#dropdownbuttonelement), [`SplitButtonElement`](../elements/buttons.md#splitbuttonelement), [`ToggleSplitButtonElement`](../elements/buttons.md#togglesplitbuttonelement).

## `CheckedState`

Sets the three-state value (`null` = indeterminate). Active only when `IsThreeState` is true.

```csharp
ToggleButtonElement CheckedState(this ToggleButtonElement el, bool? state)
```

Declared in `ElementExtensions`.

**Applies to:** [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `state` | `bool?` |  |

## `Click`

Wires a click handler (sets [`ButtonElement.OnClick`](../elements/buttons.md#buttonelement)). Passing `null` clears any existing handler.

```csharp
ButtonElement Click(this ButtonElement el, Action? handler)
HyperlinkButtonElement Click(this HyperlinkButtonElement el, Action? handler)
RepeatButtonElement Click(this RepeatButtonElement el, Action? handler)
SplitButtonElement Click(this SplitButtonElement el, Action? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Click(ButtonElement, Action?)` | Wires a click handler (sets [`ButtonElement.OnClick`](../elements/buttons.md#buttonelement)). Passing `null` clears any existing handler. |
| `Click(HyperlinkButtonElement, Action?)` | Wires a click handler (sets [`HyperlinkButtonElement.OnClick`](../elements/buttons.md#hyperlinkbuttonelement)). Passing `null` clears. |
| `Click(RepeatButtonElement, Action?)` | Wires a click handler that fires repeatedly while held. Passing `null` clears. |
| `Click(SplitButtonElement, Action?)` | Wires the primary-button click handler. Passing `null` clears. |

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement), [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement), [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement), [`SplitButtonElement`](../elements/buttons.md#splitbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `Command`

Binds a [`Command`](../input/Command.md) to a custom-content [`ButtonElement`](../elements/buttons.md#buttonelement) by lifting it onto the typed [`ButtonElement.Command`](../elements/buttons.md#buttonelement) property (issues #133, #153, unified in #637). The reconciler invokes Execute/ExecuteAsync on click, folds the command's [`Command.IsEnabled`](../input/Command.md#isenabled) into the IsEnabled descriptor entry (re-applied when the command's derived enabled state changes — field-aware, not on every render — and coexisting with `.IsDisabledFocusable()`'s coercion), and flows Description / Accelerator / AccessKey — all field-aware, with no per-render [`ButtonElement.Setters`](../elements/buttons.md#buttonelement) lambda. Pairs with `Button(content)` so icon-plus-label buttons auto-disable like the `Button(Command)` factory. The modifier makes the command fully take over: any [`ButtonElement.OnClick`](../elements/buttons.md#buttonelement) already on the element is cleared so the command — not a stale click handler — dispatches (issue #637; matches the pre-#637 modifier, and the `Button(Command)` factory which builds the element with no `OnClick`). A raw `.Set(...)` setter runs after the command metadata and therefore overrides it.

```csharp
ButtonElement Command(this ButtonElement el, Command command)
HyperlinkButtonElement Command(this HyperlinkButtonElement el, Command command)
RepeatButtonElement Command(this RepeatButtonElement el, Command command)
ToggleButtonElement Command(this ToggleButtonElement el, Command command)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Command(ButtonElement, Command)` | Binds a [`Command`](../input/Command.md) to a custom-content [`ButtonElement`](../elements/buttons.md#buttonelement) by lifting it onto the typed [`ButtonElement.Command`](../elements/buttons.md#buttonelement) property (issues #133, #153, unified in #637). The reconciler invokes Execute/ExecuteAsync on click, folds the command's [`Command.IsEnabled`](../input/Command.md#isenabled) into the IsEnabled descriptor entry (re-applied when the command's derived enabled state changes — field-aware, not on every render — and coexisting with `.IsDisabledFocusable()`'s coercion), and flows Description / Accelerator / AccessKey — all field-aware, with no per-render [`ButtonElement.Setters`](../elements/buttons.md#buttonelement) lambda. Pairs with `Button(content)` so icon-plus-label buttons auto-disable like the `Button(Command)` factory. The modifier makes the command fully take over: any [`ButtonElement.OnClick`](../elements/buttons.md#buttonelement) already on the element is cleared so the command — not a stale click handler — dispatches (issue #637; matches the pre-#637 modifier, and the `Button(Command)` factory which builds the element with no `OnClick`). A raw `.Set(...)` setter runs after the command metadata and therefore overrides it. |
| `Command(HyperlinkButtonElement, Command)` | Binds a [`Command`](../input/Command.md) to a [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement), clearing any existing `OnClick` so the command takes over dispatch. See [`Command`](#command). (issues #133, #637) |
| `Command(RepeatButtonElement, Command)` | Binds a [`Command`](../input/Command.md) to a [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement), clearing any existing `OnClick` so the command takes over dispatch. See [`Command`](#command). (issues #133, #637) |
| `Command(ToggleButtonElement, Command)` | Binds a [`Command`](../input/Command.md) to a [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement). The command fires on each toggle (check and uncheck), matching the `ToggleButton(Command)` factory; any existing toggle callback (`OnIsCheckedChanged` / `OnCheckedStateChanged`) is cleared so the command takes over dispatch. See [`Command`](#command). (issues #133, #637) |

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement), [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement), [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement), [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |

## `Delay`

```csharp
RepeatButtonElement Delay(this RepeatButtonElement el, int delay)
```

Declared in `ElementExtensions`.

**Applies to:** [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `delay` | `int` |  |

## `DisabledFocusable`

Keeps the button keyboard-focusable while presenting it as disabled (visually dimmed, click suppressed). Use for submit buttons gated on validation: a true `.IsEnabled(false)` removes the button from tab order, which combined with commit-on-blur inputs like NumberBox/ DatePicker produces a focus trap where Tab skips a Submit that is \*about\* to become valid. Conceptually the Fluent UI React `disabledFocusable` / ARIA `aria-disabled` pattern; UIA still sees the button as enabled (a custom AutomationPeer override for full AT "unavailable" reporting is a tracked follow-up).

```csharp
ButtonElement DisabledFocusable(this ButtonElement el, bool disabled = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsDisabledFocusable() — aligns with WinUI naming (see #268).

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `disabled` | `bool` | `true` |  |

## `Interval`

```csharp
RepeatButtonElement Interval(this RepeatButtonElement el, int interval)
```

Declared in `ElementExtensions`.

**Applies to:** [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `interval` | `int` |  |

## `IsDisabledFocusable`

Keeps the button keyboard-focusable while presenting it as disabled (visually dimmed, click suppressed). Use for submit buttons gated on validation: a true `.IsEnabled(false)` removes the button from tab order, which combined with commit-on-blur inputs like NumberBox/ DatePicker produces a focus trap where Tab skips a Submit that is \*about\* to become valid. Conceptually the Fluent UI React `disabledFocusable` / ARIA `aria-disabled` pattern; UIA still sees the button as enabled (a custom AutomationPeer override for full AT "unavailable" reporting is a tracked follow-up).

```csharp
ButtonElement IsDisabledFocusable(this ButtonElement el, bool disabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `disabled` | `bool` | `true` |  |

## `IsThreeState`

Enable the three-state cycle. Pair with `.CheckedState(...)` and the `.CheckedStateChanged(...)` event fluent.

```csharp
ToggleButtonElement IsThreeState(this ToggleButtonElement el, bool isThreeState = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isThreeState` | `bool` | `true` |  |

## `NavigateUri`

Sets the navigation target for the hyperlink. Pairs naturally with the `HyperlinkButton(Command)` factory when a command-driven hyperlink also needs an external URI.

```csharp
HyperlinkButtonElement NavigateUri(this HyperlinkButtonElement el, Uri uri)
```

Declared in `ElementExtensions`.

**Applies to:** [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `uri` | `Uri` |  |

## `SubtleButton`

Applies the WinUI `SubtleButtonStyle` — chromeless transparent-background button look. Theme-aware. Applied at mount only — see [`ApplyStyle`](appearance.md#applystyle).

```csharp
ButtonElement SubtleButton(this ButtonElement el)
DropDownButtonElement SubtleButton(this DropDownButtonElement el)
SplitButtonElement SubtleButton(this SplitButtonElement el)
ToggleSplitButtonElement SubtleButton(this ToggleSplitButtonElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement), [`DropDownButtonElement`](../elements/buttons.md#dropdownbuttonelement), [`SplitButtonElement`](../elements/buttons.md#splitbuttonelement), [`ToggleSplitButtonElement`](../elements/buttons.md#togglesplitbuttonelement).

## `TextLink`

Applies the WinUI `TextBlockButtonStyle` — chromeless inline-link rendering. Use for the "Learn more" pattern inside body text. Theme-aware. Applied at mount only — see [`ApplyStyle`](appearance.md#applystyle).

```csharp
ButtonElement TextLink(this ButtonElement el)
HyperlinkButtonElement TextLink(this HyperlinkButtonElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement), [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement).
