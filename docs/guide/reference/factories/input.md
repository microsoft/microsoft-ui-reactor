# Factories: Input

Text boxes, check boxes, combo boxes, sliders, pickers and other input controls.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Controls](../../controls.md), [Forms and Input](../../forms.md)

## `AutoSuggest`

Creates an auto-suggest element with async search.

```csharp
AutoSuggestElement<T> AutoSuggest<T>(T? selected, Action<T?>? onSelected = null, Func<string, CancellationToken, Task<IReadOnlyList<T>>>? search = null, Func<T, string>? displayText = null, string? placeholderText = null, int debounceMs = 300, Func<T, Element>? template = null)
```

Declared in `AutoSuggestDsl`.

**Returns:** [`AutoSuggestElement<T>`](../elements/input.md#autosuggestelementt).

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

## `AutoSuggestBox`

Creates an [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement). `text` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the WinUI control own its text.

```csharp
AutoSuggestBoxElement AutoSuggestBox(Optional<string> text = default, Action<string>? onTextChanged = null, Action<string>? onQuerySubmitted = null)
```

**Returns:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | [`Optional<string>`](../components/Optional.md) | `default` |  |
| `onTextChanged` | `Action<string>?` | `null` |  |
| `onQuerySubmitted` | `Action<string>?` | `null` |  |

## `CalendarDatePicker`

Creates a [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement). `date` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its date. Pass `null` to explicitly control the date to "no selection".

```csharp
CalendarDatePickerElement CalendarDatePicker(Optional<DateTimeOffset?> date = default, Action<DateTimeOffset?>? onDateChanged = null)
```

**Returns:** [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `date` | [`Optional<DateTimeOffset?>`](../components/Optional.md) | `default` |  |
| `onDateChanged` | `Action<DateTimeOffset?>?` | `null` |  |

## `CalendarView`

```csharp
CalendarViewElement CalendarView()
```

**Returns:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

## `CheckBox`

Creates a check box with declarative content.

```csharp
CheckBoxElement CheckBox(Optional<bool?> isChecked, Action<bool>? onIsCheckedChanged, Element content)
CheckBoxElement CheckBox(Optional<bool?> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? label = null)
```

| Overload | Description |
| --- | --- |
| `CheckBox(Optional<bool?>, Action<bool>?, Element)` | Creates a check box with declarative content. |
| `CheckBox(Optional<bool?>, Action<bool>?, string?)` | Creates a [`CheckBoxElement`](../elements/input.md#checkboxelement). `isChecked` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the WinUI control own its checked state. |

**Returns:** [`CheckBoxElement`](../elements/input.md#checkboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isChecked` | [`Optional<bool?>`](../components/Optional.md) | `default` |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `label` | `string?` | `null` |  |

## `ColorPicker`

Creates a [`ColorPickerElement`](../elements/input.md#colorpickerelement). `color` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its color.

```csharp
ColorPickerElement ColorPicker(Optional<Color> color = default, Action<Color>? onColorChanged = null)
```

**Returns:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `color` | [`Optional<Color>`](../components/Optional.md) | `default` |  |
| `onColorChanged` | `Action<Color>?` | `null` |  |

## `ComboBox`

Creates a [`ComboBoxElement`](../elements/input.md#comboboxelement) from element items. `selectedIndex` is taken as-is — pass [`Optional.Unset`](../components/Optional.md#unset) for uncontrolled, or any int (implicit `int → Optional<int>`) for controlled. Both trailing args are required (rather than defaulted) to disambiguate from the `string[]` overload — `string` has an implicit conversion to `Element`, so a collection-expression call like `ComboBox(["A","B"], selectedIndex: 0)` would otherwise resolve to either.

```csharp
ComboBoxElement ComboBox(Element[] itemElements, Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged)
ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

| Overload | Description |
| --- | --- |
| `ComboBox(Element[], Optional<int>, Action<int>?)` | Creates a [`ComboBoxElement`](../elements/input.md#comboboxelement) from element items. `selectedIndex` is taken as-is — pass [`Optional.Unset`](../components/Optional.md#unset) for uncontrolled, or any int (implicit `int → Optional<int>`) for controlled. Both trailing args are required (rather than defaulted) to disambiguate from the `string[]` overload — `string` has an implicit conversion to `Element`, so a collection-expression call like `ComboBox(["A","B"], selectedIndex: 0)` would otherwise resolve to either. |
| `ComboBox(string[], Optional<int>, Action<int>?)` | Creates a [`ComboBoxElement`](../elements/input.md#comboboxelement). `selectedIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its selection. |

**Returns:** [`ComboBoxElement`](../elements/input.md#comboboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `itemElements` | [`Element[]`](../elements/Element.md) |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `items` | `string[]` |  |  |

## `DatePicker`

Creates a [`DatePickerElement`](../elements/input.md#datepickerelement). `date` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its date.

```csharp
DatePickerElement DatePicker(Optional<DateTimeOffset> date = default, Action<DateTimeOffset>? onDateChanged = null)
```

**Returns:** [`DatePickerElement`](../elements/input.md#datepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `date` | [`Optional<DateTimeOffset>`](../components/Optional.md) | `default` |  |
| `onDateChanged` | `Action<DateTimeOffset>?` | `null` |  |

## `MaskedTextBox`

Creates a [`MaskedTextBoxElement`](../elements/input.md#maskedtextboxelement) — a Reactor-original masked input control (cf. WinForms `MaskedTextBox`; WinUI ships no first-party equivalent).

```csharp
MaskedTextBoxElement MaskedTextBox(string value, Action<string>? onChanged = null, string? mask = null, string? header = null, char placeholder = '_')
```

Declared in `MaskedTextBoxDsl`.

**Returns:** [`MaskedTextBoxElement`](../elements/input.md#maskedtextboxelement).

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

## `NumberBox`

Creates a [`NumberBoxElement`](../elements/input.md#numberboxelement). `value` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the WinUI control own its value.

```csharp
NumberBoxElement NumberBox(Optional<double> value = default, Action<double>? onValueChanged = null, string? header = null)
```

**Returns:** [`NumberBoxElement`](../elements/input.md#numberboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<double>`](../components/Optional.md) | `default` |  |
| `onValueChanged` | `Action<double>?` | `null` |  |
| `header` | `string?` | `null` |  |

## `PasswordBox`

Creates a [`PasswordBoxElement`](../elements/input.md#passwordboxelement). `password` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the WinUI control own its password.

```csharp
PasswordBoxElement PasswordBox(Optional<string> password = default, Action<string>? onPasswordChanged = null, string? placeholderText = null)
```

**Returns:** [`PasswordBoxElement`](../elements/input.md#passwordboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `password` | [`Optional<string>`](../components/Optional.md) | `default` |  |
| `onPasswordChanged` | `Action<string>?` | `null` |  |
| `placeholderText` | `string?` | `null` |  |

## `RadioButton`

Creates a radio button with declarative content.

```csharp
RadioButtonElement RadioButton(Element content, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? groupName = null)
RadioButtonElement RadioButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? groupName = null)
```

| Overload | Description |
| --- | --- |
| `RadioButton(Element, Optional<bool>, Action<bool>?, string?)` | Creates a radio button with declarative content. |
| `RadioButton(string, Optional<bool>, Action<bool>?, string?)` | Creates a [`RadioButtonElement`](../elements/input.md#radiobuttonelement). `isChecked` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its checked state. |

**Returns:** [`RadioButtonElement`](../elements/input.md#radiobuttonelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `isChecked` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `groupName` | `string?` | `null` |  |
| `label` | `string` |  |  |

## `RadioButtons`

Creates a [`RadioButtonsElement`](../elements/input.md#radiobuttonselement). `selectedIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its selection.

```csharp
RadioButtonsElement RadioButtons(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

**Returns:** [`RadioButtonsElement`](../elements/input.md#radiobuttonselement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | `string[]` |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `RatingControl`

Creates a [`RatingControlElement`](../elements/input.md#ratingcontrolelement). `value` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its value.

```csharp
RatingControlElement RatingControl(Optional<double> value = default, Action<double>? onValueChanged = null)
```

**Returns:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<double>`](../components/Optional.md) | `default` |  |
| `onValueChanged` | `Action<double>?` | `null` |  |

## `RichEditBox`

Creates a [`RichEditBoxElement`](../elements/input.md#richeditboxelement). `text` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the WinUI control own its text (uncontrolled). Pass any string (implicit `T → Optional<T>`) to control the text from a parent state.

```csharp
RichEditBoxElement RichEditBox(Optional<string> text = default, Action<string>? onTextChanged = null)
```

**Returns:** [`RichEditBoxElement`](../elements/input.md#richeditboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | [`Optional<string>`](../components/Optional.md) | `default` |  |
| `onTextChanged` | `Action<string>?` | `null` |  |

## `Slider`

Creates a [`SliderElement`](../elements/input.md#sliderelement). `value` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its value.

```csharp
SliderElement Slider(Optional<double> value = default, double min = 0, double max = 100, Action<double>? onValueChanged = null)
```

**Returns:** [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<double>`](../components/Optional.md) | `default` |  |
| `min` | `double` | `0` |  |
| `max` | `double` | `100` |  |
| `onValueChanged` | `Action<double>?` | `null` |  |

## `TextBox`

Creates a [`TextBoxElement`](../elements/input.md#textboxelement) wrapping WinUI's `Microsoft.UI.Xaml.Controls.TextBox`.

```csharp
TextBoxElement TextBox(Optional<string> value = default, Action<string>? onChanged = null, string? placeholderText = null, string? header = null)
```

**Returns:** [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | [`Optional<string>`](../components/Optional.md) | `default` |  |
| `onChanged` | `Action<string>?` | `null` |  |
| `placeholderText` | `string?` | `null` |  |
| `header` | `string?` | `null` |  |

## `ThreeStateCheckBox`

Creates a three-state check box with declarative content.

```csharp
CheckBoxElement ThreeStateCheckBox(Optional<bool?> checkedState, Action<bool?>? onCheckedStateChanged, Element content)
CheckBoxElement ThreeStateCheckBox(Optional<bool?> checkedState = default, Action<bool?>? onCheckedStateChanged = null, string? label = null)
```

| Overload | Description |
| --- | --- |
| `ThreeStateCheckBox(Optional<bool?>, Action<bool?>?, Element)` | Creates a three-state check box with declarative content. |
| `ThreeStateCheckBox(Optional<bool?>, Action<bool?>?, string?)` | Creates a three-state [`CheckBoxElement`](../elements/input.md#checkboxelement). `checkedState` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its state. |

**Returns:** [`CheckBoxElement`](../elements/input.md#checkboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `checkedState` | [`Optional<bool?>`](../components/Optional.md) | `default` |  |
| `onCheckedStateChanged` | `Action<bool?>?` | `null` |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `label` | `string?` | `null` |  |

## `TimePicker`

Creates a [`TimePickerElement`](../elements/input.md#timepickerelement). `time` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its time.

```csharp
TimePickerElement TimePicker(Optional<TimeSpan> time = default, Action<TimeSpan>? onTimeChanged = null)
```

**Returns:** [`TimePickerElement`](../elements/input.md#timepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `time` | [`Optional<TimeSpan>`](../components/Optional.md) | `default` |  |
| `onTimeChanged` | `Action<TimeSpan>?` | `null` |  |

## `ToggleSwitch`

Creates a [`ToggleSwitchElement`](../elements/input.md#toggleswitchelement). `isOn` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its on/off state.

```csharp
ToggleSwitchElement ToggleSwitch(Optional<bool> isOn = default, Action<bool>? onIsOnChanged = null, string? onContent = null, string? offContent = null, string? header = null)
```

**Returns:** [`ToggleSwitchElement`](../elements/input.md#toggleswitchelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isOn` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `onIsOnChanged` | `Action<bool>?` | `null` |  |
| `onContent` | `string?` | `null` |  |
| `offContent` | `string?` | `null` |  |
| `header` | `string?` | `null` |  |
