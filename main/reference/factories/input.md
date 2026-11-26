---
title: "Factories: Input"
description: "Text boxes, check boxes, combo boxes, sliders, pickers and other input controls."
sidebar:
  label: "Input"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## `AutoSuggest` [#autosuggest]

Creates an auto-suggest element with async search.

```csharp
AutoSuggestElement<T> AutoSuggest<T>(T? selected, Action<T?>? onSelected = null, Func<string, CancellationToken, Task<IReadOnlyList<T>>>? search = null, Func<T, string>? displayText = null, string? placeholderText = null, int debounceMs = 300, Func<T, Element>? template = null)
```

Declared in `AutoSuggestDsl`. Call it through `using static Microsoft.UI.Reactor.Controls.AutoSuggestDsl;`.

**Returns:** [`AutoSuggestElement<T>`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestelementt).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `selected` | `T?` |  |  |
| `onSelected` | `Action<T?>?` | `null` |  |
| `search` | `Func<string, CancellationToken, Task<IReadOnlyList<T>>>?` | `null` |  |
| `displayText` | `Func<T, string>?` | `null` |  |
| `placeholderText` | `string?` | `null` |  |
| `debounceMs` | `int` | `300` |  |
| `template` | `Func<T, Element>?` | `null` |  |

## `AutoSuggestBox` [#autosuggestbox]

Creates an [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement). `text` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the WinUI control own its text.

```csharp
AutoSuggestBoxElement AutoSuggestBox(Optional<string> text = default, Action<string>? onTextChanged = null, Action<string>? onQuerySubmitted = null)
```

**Returns:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onTextChanged` | `Action<string>?` | `null` |  |
| `onQuerySubmitted` | `Action<string>?` | `null` |  |

## `CalendarDatePicker` [#calendardatepicker]

Creates a [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement). `date` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its date. Pass `null` to explicitly control the date to "no selection".

```csharp
CalendarDatePickerElement CalendarDatePicker(Optional<DateTimeOffset?> date = default, Action<DateTimeOffset?>? onDateChanged = null)
```

**Returns:** [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `date` | [`Optional<DateTimeOffset?>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onDateChanged` | `Action<DateTimeOffset?>?` | `null` |  |

## `CalendarView` [#calendarview]

```csharp
CalendarViewElement CalendarView()
```

**Returns:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

## `CheckBox` [#checkbox]

```csharp
CheckBoxElement CheckBox(Optional<bool?> isChecked, Action<bool>? onIsCheckedChanged, Element content)
CheckBoxElement CheckBox(Optional<bool?> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? label = null)
```

| Overload | Description |
| --- | --- |
| `CheckBox(Optional<bool?>, Action<bool>?, Element)` | Creates a check box with declarative content. |
| `CheckBox(Optional<bool?>, Action<bool>?, string?)` | Creates a [`CheckBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#checkboxelement). `isChecked` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the WinUI control own its checked state. |

**Returns:** [`CheckBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#checkboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isChecked` | [`Optional<bool?>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `label` | `string?` | `null` |  |

## `ColorPicker` [#colorpicker]

Creates a [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement). `color` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its color.

```csharp
ColorPickerElement ColorPicker(Optional<Color> color = default, Action<Color>? onColorChanged = null)
```

**Returns:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `color` | [`Optional<Color>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onColorChanged` | `Action<Color>?` | `null` |  |

## `ComboBox` [#combobox]

```csharp
ComboBoxElement ComboBox(Element[] itemElements, Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged)
ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

| Overload | Description |
| --- | --- |
| `ComboBox(Element[], Optional<int>, Action<int>?)` | Creates a [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement) from element items. `selectedIndex` is taken as-is — pass [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) for uncontrolled, or any int (implicit `int → Optional<int>`) for controlled. Both trailing args are required (rather than defaulted) to disambiguate from the `string[]` overload — `string` has an implicit conversion to `Element`, so a collection-expression call like `ComboBox(["A","B"], selectedIndex: 0)` would otherwise resolve to either. |
| `ComboBox(string[], Optional<int>, Action<int>?)` | Creates a [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement). `selectedIndex` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its selection. |

**Returns:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `itemElements` | [`Element[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `selectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `items` | `string[]` |  |  |

## `DatePicker` [#datepicker]

Creates a [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement). `date` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its date.

```csharp
DatePickerElement DatePicker(Optional<DateTimeOffset> date = default, Action<DateTimeOffset>? onDateChanged = null)
```

**Returns:** [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `date` | [`Optional<DateTimeOffset>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onDateChanged` | `Action<DateTimeOffset>?` | `null` |  |

## `MaskedTextBox` [#maskedtextbox]

Creates a [`MaskedTextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#maskedtextboxelement) — a Reactor-original masked input control (cf. WinForms `MaskedTextBox`; WinUI ships no first-party equivalent).

```csharp
MaskedTextBoxElement MaskedTextBox(string value, Action<string>? onChanged = null, string? mask = null, string? header = null, char placeholder = '_')
```

Declared in `MaskedTextBoxDsl`. Call it through `using static Microsoft.UI.Reactor.Controls.MaskedTextBoxDsl;`.

**Returns:** [`MaskedTextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#maskedtextboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `string` |  |  |
| `onChanged` | `Action<string>?` | `null` |  |
| `mask` | `string?` | `null` |  |
| `header` | `string?` | `null` |  |
| `placeholder` | `char` | `'_'` |  |

**Remarks**

Named to align with WinUI's `TextBox` and Reactor's `TextBox()` factory. (issue #389)

## `NumberBox` [#numberbox]

Creates a [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement). `value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the WinUI control own its value.

```csharp
NumberBoxElement NumberBox(Optional<double> value = default, Action<double>? onValueChanged = null, string? header = null)
```

**Returns:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<double>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onValueChanged` | `Action<double>?` | `null` |  |
| `header` | `string?` | `null` |  |

## `PasswordBox` [#passwordbox]

Creates a [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement). `password` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the WinUI control own its password.

```csharp
PasswordBoxElement PasswordBox(Optional<string> password = default, Action<string>? onPasswordChanged = null, string? placeholderText = null)
```

**Returns:** [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `password` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onPasswordChanged` | `Action<string>?` | `null` |  |
| `placeholderText` | `string?` | `null` |  |

## `RadioButton` [#radiobutton]

```csharp
RadioButtonElement RadioButton(Element content, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? groupName = null)
RadioButtonElement RadioButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? groupName = null)
```

| Overload | Description |
| --- | --- |
| `RadioButton(Element, Optional<bool>, Action<bool>?, string?)` | Creates a radio button with declarative content. |
| `RadioButton(string, Optional<bool>, Action<bool>?, string?)` | Creates a [`RadioButtonElement`](/microsoft-ui-reactor/main/reference/elements/input#radiobuttonelement). `isChecked` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its checked state. |

**Returns:** [`RadioButtonElement`](/microsoft-ui-reactor/main/reference/elements/input#radiobuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `isChecked` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `groupName` | `string?` | `null` |  |
| `label` | `string` |  |  |

## `RadioButtons` [#radiobuttons]

Creates a [`RadioButtonsElement`](/microsoft-ui-reactor/main/reference/elements/input#radiobuttonselement). `selectedIndex` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its selection.

```csharp
RadioButtonsElement RadioButtons(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

**Returns:** [`RadioButtonsElement`](/microsoft-ui-reactor/main/reference/elements/input#radiobuttonselement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | `string[]` |  |  |
| `selectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `RatingControl` [#ratingcontrol]

Creates a [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement). `value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its value.

```csharp
RatingControlElement RatingControl(Optional<double> value = default, Action<double>? onValueChanged = null)
```

**Returns:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<double>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onValueChanged` | `Action<double>?` | `null` |  |

## `RichEditBox` [#richeditbox]

Creates a [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement). `text` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the WinUI control own its text (uncontrolled). Pass any string (implicit `T → Optional<T>`) to control the text from a parent state.

```csharp
RichEditBoxElement RichEditBox(Optional<string> text = default, Action<string>? onTextChanged = null)
```

**Returns:** [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onTextChanged` | `Action<string>?` | `null` |  |

## `Slider` [#slider]

Creates a [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement). `value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its value.

```csharp
SliderElement Slider(Optional<double> value = default, double min = 0, double max = 100, Action<double>? onValueChanged = null)
```

**Returns:** [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<double>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `min` | `double` | `0` |  |
| `max` | `double` | `100` |  |
| `onValueChanged` | `Action<double>?` | `null` |  |

## `TextBox` [#textbox]

Creates a [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement) wrapping WinUI's `Microsoft.UI.Xaml.Controls.TextBox`.

```csharp
TextBoxElement TextBox(Optional<string> value = default, Action<string>? onChanged = null, string? placeholderText = null, string? header = null)
```

**Returns:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onChanged` | `Action<string>?` | `null` |  |
| `placeholderText` | `string?` | `null` |  |
| `header` | `string?` | `null` |  |

## `ThreeStateCheckBox` [#threestatecheckbox]

```csharp
CheckBoxElement ThreeStateCheckBox(Optional<bool?> checkedState, Action<bool?>? onCheckedStateChanged, Element content)
CheckBoxElement ThreeStateCheckBox(Optional<bool?> checkedState = default, Action<bool?>? onCheckedStateChanged = null, string? label = null)
```

| Overload | Description |
| --- | --- |
| `ThreeStateCheckBox(Optional<bool?>, Action<bool?>?, Element)` | Creates a three-state check box with declarative content. |
| `ThreeStateCheckBox(Optional<bool?>, Action<bool?>?, string?)` | Creates a three-state [`CheckBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#checkboxelement). `checkedState` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its state. |

**Returns:** [`CheckBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#checkboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `checkedState` | [`Optional<bool?>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onCheckedStateChanged` | `Action<bool?>?` | `null` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `label` | `string?` | `null` |  |

## `TimePicker` [#timepicker]

Creates a [`TimePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#timepickerelement). `time` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its time.

```csharp
TimePickerElement TimePicker(Optional<TimeSpan> time = default, Action<TimeSpan>? onTimeChanged = null)
```

**Returns:** [`TimePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#timepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `time` | [`Optional<TimeSpan>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onTimeChanged` | `Action<TimeSpan>?` | `null` |  |

## `ToggleSwitch` [#toggleswitch]

Creates a [`ToggleSwitchElement`](/microsoft-ui-reactor/main/reference/elements/input#toggleswitchelement). `isOn` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its on/off state.

```csharp
ToggleSwitchElement ToggleSwitch(Optional<bool> isOn = default, Action<bool>? onIsOnChanged = null, string? onContent = null, string? offContent = null, string? header = null)
```

**Returns:** [`ToggleSwitchElement`](/microsoft-ui-reactor/main/reference/elements/input#toggleswitchelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isOn` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onIsOnChanged` | `Action<bool>?` | `null` |  |
| `onContent` | `string?` | `null` |  |
| `offContent` | `string?` | `null` |  |
| `header` | `string?` | `null` |  |
