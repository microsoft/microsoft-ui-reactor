---
title: "Control modifiers: Buttons"
description: "Modifiers that only the controls in the Buttons area have."
sidebar:
  label: "Buttons"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Commanding](/microsoft-ui-reactor/main/guides/commanding)

## `AccentButton` [#accentbutton]

Applies the WinUI `AccentButtonStyle` — the accent-color primary-button look. The Style is resolved from `Application.Current.Resources` once; its colors still follow light/dark/contrast switches because the Style's own setters use `{ThemeResource}` values, which WinUI re-resolves on theme change.

**Applied at mount only.** Like [`ApplyStyle`](/microsoft-ui-reactor/main/reference/modifiers/appearance#applystyle) this runs through `OnMount`, which the reconciler gates on first mount — so conditionally chaining it (e.g. `isPrimary? btn.AccentButton(): btn`) will *not* apply or remove the style when the condition flips on an in-place update. Give the two variants different [`WithKey`](/microsoft-ui-reactor/main/reference/modifiers/lifecycle#withkey) values to force a remount when the style must change.

```csharp
ButtonElement AccentButton(this ButtonElement el)
DropDownButtonElement AccentButton(this DropDownButtonElement el)
SplitButtonElement AccentButton(this SplitButtonElement el)
ToggleSplitButtonElement AccentButton(this ToggleSplitButtonElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement), [`DropDownButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#dropdownbuttonelement), [`SplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#splitbuttonelement), [`ToggleSplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglesplitbuttonelement).

## `CheckedState` [#checkedstate]

Sets the three-state value (`null` = indeterminate). Active only when `IsThreeState` is true.

```csharp
ToggleButtonElement CheckedState(this ToggleButtonElement el, bool? state)
```

Declared in `ElementExtensions`.

**Applies to:** [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `state` | `bool?` |  |

## `Click` [#click]

```csharp
ButtonElement Click(this ButtonElement el, Action? handler)
HyperlinkButtonElement Click(this HyperlinkButtonElement el, Action? handler)
RepeatButtonElement Click(this RepeatButtonElement el, Action? handler)
SplitButtonElement Click(this SplitButtonElement el, Action? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Click(ButtonElement, Action?)` | Wires a click handler (sets [`ButtonElement.OnClick`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement)). Passing `null` clears any existing handler. |
| `Click(HyperlinkButtonElement, Action?)` | Wires a click handler (sets [`HyperlinkButtonElement.OnClick`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement)). Passing `null` clears. |
| `Click(RepeatButtonElement, Action?)` | Wires a click handler that fires repeatedly while held. Passing `null` clears. |
| `Click(SplitButtonElement, Action?)` | Wires the primary-button click handler. Passing `null` clears. |

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement), [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement), [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement), [`SplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#splitbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `Command` [#command]

```csharp
ButtonElement Command(this ButtonElement el, Command command)
HyperlinkButtonElement Command(this HyperlinkButtonElement el, Command command)
RepeatButtonElement Command(this RepeatButtonElement el, Command command)
ToggleButtonElement Command(this ToggleButtonElement el, Command command)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Command(ButtonElement, Command)` | Binds a [`Command`](/microsoft-ui-reactor/main/reference/input/Command) to a custom-content [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement) by lifting it onto the typed [`ButtonElement.Command`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement) property (issues #133, #153, unified in #637). The reconciler invokes Execute/ExecuteAsync on click, folds the command's [`Command.IsEnabled`](/microsoft-ui-reactor/main/reference/input/Command#isenabled) into the IsEnabled descriptor entry (re-applied when the command's derived enabled state changes — field-aware, not on every render — and coexisting with `.IsDisabledFocusable()`'s coercion), and flows Description / Accelerator / AccessKey — all field-aware, with no per-render [`ButtonElement.Setters`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement) lambda. Pairs with `Button(content)` so icon-plus-label buttons auto-disable like the `Button(Command)` factory. The modifier makes the command fully take over: any [`ButtonElement.OnClick`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement) already on the element is cleared so the command — not a stale click handler — dispatches (issue #637; matches the pre-#637 modifier, and the `Button(Command)` factory which builds the element with no `OnClick`). A raw `.Set(...)` setter runs after the command metadata and therefore overrides it. |
| `Command(HyperlinkButtonElement, Command)` | Binds a [`Command`](/microsoft-ui-reactor/main/reference/input/Command) to a [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement), clearing any existing `OnClick` so the command takes over dispatch. See [`Command`](#command). (issues #133, #637) |
| `Command(RepeatButtonElement, Command)` | Binds a [`Command`](/microsoft-ui-reactor/main/reference/input/Command) to a [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement), clearing any existing `OnClick` so the command takes over dispatch. See [`Command`](#command). (issues #133, #637) |
| `Command(ToggleButtonElement, Command)` | Binds a [`Command`](/microsoft-ui-reactor/main/reference/input/Command) to a [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement). The command fires on each toggle (check and uncheck), matching the `ToggleButton(Command)` factory; any existing toggle callback (`OnIsCheckedChanged` / `OnCheckedStateChanged`) is cleared so the command takes over dispatch. See [`Command`](#command). (issues #133, #637) |

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement), [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement), [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement), [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `command` | [`Command`](/microsoft-ui-reactor/main/reference/input/Command) |  |

## `Delay` [#delay]

```csharp
RepeatButtonElement Delay(this RepeatButtonElement el, int delay)
```

Declared in `ElementExtensions`.

**Applies to:** [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `delay` | `int` |  |

## `DisabledFocusable` [#disabledfocusable]

Keeps the button keyboard-focusable while presenting it as disabled (visually dimmed, click suppressed). Use for submit buttons gated on validation: a true `.IsEnabled(false)` removes the button from tab order, which combined with commit-on-blur inputs like NumberBox/ DatePicker produces a focus trap where Tab skips a Submit that is \*about\* to become valid. Conceptually the Fluent UI React `disabledFocusable` / ARIA `aria-disabled` pattern; UIA still sees the button as enabled (a custom AutomationPeer override for full AT "unavailable" reporting is a tracked follow-up).

```csharp
ButtonElement DisabledFocusable(this ButtonElement el, bool disabled = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsDisabledFocusable() — aligns with WinUI naming (see #268).

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `disabled` | `bool` | `true` |  |

## `Interval` [#interval]

```csharp
RepeatButtonElement Interval(this RepeatButtonElement el, int interval)
```

Declared in `ElementExtensions`.

**Applies to:** [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `interval` | `int` |  |

## `IsDisabledFocusable` [#isdisabledfocusable]

Keeps the button keyboard-focusable while presenting it as disabled (visually dimmed, click suppressed). Use for submit buttons gated on validation: a true `.IsEnabled(false)` removes the button from tab order, which combined with commit-on-blur inputs like NumberBox/ DatePicker produces a focus trap where Tab skips a Submit that is \*about\* to become valid. Conceptually the Fluent UI React `disabledFocusable` / ARIA `aria-disabled` pattern; UIA still sees the button as enabled (a custom AutomationPeer override for full AT "unavailable" reporting is a tracked follow-up).

```csharp
ButtonElement IsDisabledFocusable(this ButtonElement el, bool disabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `disabled` | `bool` | `true` |  |

## `IsThreeState` [#isthreestate]

Enable the three-state cycle. Pair with `.CheckedState(...)` and the `.CheckedStateChanged(...)` event fluent.

```csharp
ToggleButtonElement IsThreeState(this ToggleButtonElement el, bool isThreeState = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isThreeState` | `bool` | `true` |  |

## `NavigateUri` [#navigateuri]

Sets the navigation target for the hyperlink. Pairs naturally with the `HyperlinkButton(Command)` factory when a command-driven hyperlink also needs an external URI.

```csharp
HyperlinkButtonElement NavigateUri(this HyperlinkButtonElement el, Uri uri)
```

Declared in `ElementExtensions`.

**Applies to:** [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `uri` | `Uri` |  |

## `SubtleButton` [#subtlebutton]

Applies the WinUI `SubtleButtonStyle` — chromeless transparent-background button look. Theme-aware. Applied at mount only — see [`ApplyStyle`](/microsoft-ui-reactor/main/reference/modifiers/appearance#applystyle).

```csharp
ButtonElement SubtleButton(this ButtonElement el)
DropDownButtonElement SubtleButton(this DropDownButtonElement el)
SplitButtonElement SubtleButton(this SplitButtonElement el)
ToggleSplitButtonElement SubtleButton(this ToggleSplitButtonElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement), [`DropDownButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#dropdownbuttonelement), [`SplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#splitbuttonelement), [`ToggleSplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglesplitbuttonelement).

## `TextLink` [#textlink]

Applies the WinUI `TextBlockButtonStyle` — chromeless inline-link rendering. Use for the "Learn more" pattern inside body text. Theme-aware. Applied at mount only — see [`ApplyStyle`](/microsoft-ui-reactor/main/reference/modifiers/appearance#applystyle).

```csharp
ButtonElement TextLink(this ButtonElement el)
HyperlinkButtonElement TextLink(this HyperlinkButtonElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement), [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement).
