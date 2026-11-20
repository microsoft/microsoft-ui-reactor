# Control modifiers: Input

Modifiers that only the controls in the Input area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Controls](../../controls.md), [Forms and Input](../../forms.md)

## `AcceptsExpression`

Whether the user can type arithmetic expressions (e.g. `2*3+1`) that resolve on commit.

```csharp
NumberBoxElement AcceptsExpression(this NumberBoxElement el, bool accepts = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `accepts` | `bool` | `true` |  |

## `AcceptsReturn`

Whether Enter inserts a newline (vs committing input).

```csharp
RichEditBoxElement AcceptsReturn(this RichEditBoxElement el, bool accepts = true)
TextBoxElement AcceptsReturn(this TextBoxElement el, bool accepts = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `accepts` | `bool` | `true` |  |

## `AlphaEnabled`

Whether the alpha (transparency) channel is editable.

```csharp
ColorPickerElement AlphaEnabled(this ColorPickerElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `Caption`

Promotes the existing `Caption` init property to a fluent.

```csharp
RatingControlElement Caption(this RatingControlElement el, string caption)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `caption` | `string` |  |

## `Changed`

Wires the text-changed handler. Passing `null` clears.

```csharp
MaskedTextBoxElement Changed(this MaskedTextBoxElement el, Action<string>? handler)
TextBoxElement Changed(this TextBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Changed(MaskedTextBoxElement, Action<string>?)` | Wires the text-changed handler. Passing `null` clears. |
| `Changed(TextBoxElement, Action<string>?)` | Wires the text-changed handler. Receives the new text. Passing `null` clears. |

**Applies to:** [`MaskedTextBoxElement`](../elements/input.md#maskedtextboxelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `CharacterCasing`

Forces input to upper/lower-case as the user types.

```csharp
TextBoxElement CharacterCasing(this TextBoxElement el, CharacterCasing casing)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `casing` | `CharacterCasing` |  |

## `ColorChanged`

Wires the color-changed handler. Passing `null` clears.

```csharp
ColorPickerElement ColorChanged(this ColorPickerElement el, Action<Color>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Color>?` |  |

## `ColorChannelTextInputVisible`

Whether the per-channel numeric text inputs (RGB / HSV) are shown.

```csharp
ColorPickerElement ColorChannelTextInputVisible(this ColorPickerElement el, bool visible)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `ColorSliderVisible`

Whether the lightness/value slider is shown.

```csharp
ColorPickerElement ColorSliderVisible(this ColorPickerElement el, bool visible)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `ColorSpectrumShape`

Shape of the 2D color spectrum (Box or Ring).

```csharp
ColorPickerElement ColorSpectrumShape(this ColorPickerElement el, ColorSpectrumShape shape)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `shape` | `ColorSpectrumShape` |  |

## `ColorSpectrumVisible`

Whether the 2D hue/saturation spectrum is shown.

```csharp
ColorPickerElement ColorSpectrumVisible(this ColorPickerElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `DateChanged`

Wires the date-changed handler. Receives null when the user clears the selection. Passing `null` as the handler clears it.

```csharp
CalendarDatePickerElement DateChanged(this CalendarDatePickerElement el, Action<DateTimeOffset?>? handler)
DatePickerElement DateChanged(this DatePickerElement el, Action<DateTimeOffset>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `DateChanged(CalendarDatePickerElement, Action<DateTimeOffset?>?)` | Wires the date-changed handler. Receives null when the user clears the selection. Passing `null` as the handler clears it. |
| `DateChanged(DatePickerElement, Action<DateTimeOffset>?)` | Wires the date-changed handler. Passing `null` clears. |

**Applies to:** [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement), [`DatePickerElement`](../elements/input.md#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DateTimeOffset?>?` or `Action<DateTimeOffset>?` |  |

## `DateFormat`

Display format string for the picker's text. See WinUI's `DateFormat` reference.

```csharp
CalendarDatePickerElement DateFormat(this CalendarDatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

## `DayFormat`

Display format string for the day column.

```csharp
DatePickerElement DayFormat(this DatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`DatePickerElement`](../elements/input.md#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

## `Description`

Help text rendered below the box.

```csharp
ComboBoxElement Description(this ComboBoxElement el, string description)
NumberBoxElement Description(this NumberBoxElement el, string description)
TextBoxElement Description(this TextBoxElement el, string description)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Description(ComboBoxElement, string)` | Help text rendered below the box. |
| `Description(NumberBoxElement, string)` | Sets the help/description text rendered below the box. |
| `Description(TextBoxElement, string)` | Sets the help/description text rendered below the box. |

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement), [`NumberBoxElement`](../elements/input.md#numberboxelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

## `DisplayMode`

Initial display mode (Month / Year / Decade).

```csharp
CalendarViewElement DisplayMode(this CalendarViewElement el, CalendarViewDisplayMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `CalendarViewDisplayMode` |  |

## `DropDownClosed`

Wires the drop-down-closed handler (fires on dismissal or selection). Passing `null` clears.

```csharp
ComboBoxElement DropDownClosed(this ComboBoxElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `DropDownOpened`

Wires the drop-down-opened handler. Passing `null` clears.

```csharp
ComboBoxElement DropDownOpened(this ComboBoxElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `Editable`

Sets whether the ComboBox is editable.

```csharp
ComboBoxElement Editable(this ComboBoxElement el, bool editable = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsEditable() — aligns with WinUI naming (see #268).

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `editable` | `bool` | `true` |  |

## `EmailInput`

Sets `InputScope = EmailSmtpAddress`.

```csharp
TextBoxElement EmailInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

## `FirstDayOfWeek`

Day of the week that starts each row.

```csharp
CalendarViewElement FirstDayOfWeek(this CalendarViewElement el, DayOfWeek day)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `day` | `DayOfWeek` |  |

## `Focus`

Registers this NumberBox with a FocusManager for programmatic focus control.

```csharp
NumberBoxElement Focus(this NumberBoxElement el, FocusManager fm, string fieldName, bool autoFocus = false)
TextBoxElement Focus(this TextBoxElement el, FocusManager fm, string fieldName, bool autoFocus = false)
```

Declared in `FocusExtensions`.

| Overload | Description |
| --- | --- |
| `Focus(NumberBoxElement, FocusManager, string, bool)` | Registers this NumberBox with a FocusManager for programmatic focus control. |
| `Focus(TextBoxElement, FocusManager, string, bool)` | Registers this TextBox with a FocusManager for programmatic focus control. |

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fm` | [`FocusManager`](../hooks/focus-helpers.md#focusmanager) |  |  |
| `fieldName` | `string` |  |  |
| `autoFocus` | `bool` | `false` |  |

## `Header`

Optional label rendered above the box.

```csharp
AutoSuggestBoxElement Header(this AutoSuggestBoxElement el, string header)
ComboBoxElement Header(this ComboBoxElement el, string header)
PasswordBoxElement Header(this PasswordBoxElement el, string header)
SliderElement Header(this SliderElement el, string header)
TextBoxElement Header(this TextBoxElement el, string header)
ToggleSwitchElement Header(this ToggleSwitchElement el, string header)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement), [`ComboBoxElement`](../elements/input.md#comboboxelement), [`PasswordBoxElement`](../elements/input.md#passwordboxelement), [`SliderElement`](../elements/input.md#sliderelement), [`TextBoxElement`](../elements/input.md#textboxelement), [`ToggleSwitchElement`](../elements/input.md#toggleswitchelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | `string` |  |

## `HexInputVisible`

Whether the hex code text input is shown.

```csharp
ColorPickerElement HexInputVisible(this ColorPickerElement el, bool visible)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `HueRange`

Inclusive hue bounds (0–359).

```csharp
ColorPickerElement HueRange(this ColorPickerElement el, int minHue, int maxHue)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minHue` | `int` |  |
| `maxHue` | `int` |  |

## `InitialSetValue`

Integer rating to assume when the user first interacts.

```csharp
RatingControlElement InitialSetValue(this RatingControlElement el, int value)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `int` |  |

## `InputScope`

Sets a specific [`InputScopeNameValue`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.inputscopenamevalue). Escape hatch for input scopes outside the named helpers above (e.g. `Chat`, `FormulaNumber`, `AlphanumericFullWidth`).

```csharp
TextBoxElement InputScope(this TextBoxElement el, InputScopeNameValue scope)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `scope` | `InputScopeNameValue` |  |

## `IsCalendarOpen`

Programmatically open or close the calendar popup.

```csharp
CalendarDatePickerElement IsCalendarOpen(this CalendarDatePickerElement el, bool open = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `open` | `bool` | `true` |  |

## `IsEditable`

Sets whether the ComboBox is editable.

```csharp
ComboBoxElement IsEditable(this ComboBoxElement el, bool editable = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `editable` | `bool` | `true` |  |

## `IsGroupLabelVisible`

Show month/year group label headers in the popup.

```csharp
CalendarDatePickerElement IsGroupLabelVisible(this CalendarDatePickerElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `IsOnChanged`

Wires the on/off-state-changed handler. Passing `null` clears.

```csharp
ToggleSwitchElement IsOnChanged(this ToggleSwitchElement el, Action<bool>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ToggleSwitchElement`](../elements/input.md#toggleswitchelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<bool>?` |  |

## `IsReadOnly`

Sets whether the RatingControl is read-only.

```csharp
RatingControlElement IsReadOnly(this RatingControlElement el, bool readOnly = true)
TextBoxElement IsReadOnly(this TextBoxElement el, bool readOnly = true)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `IsReadOnly(RatingControlElement, bool)` | Sets whether the RatingControl is read-only. |
| `IsReadOnly(TextBoxElement, bool)` | Sets whether the text field is read-only. |

**Applies to:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `readOnly` | `bool` | `true` |  |

## `IsSpellCheckEnabled`

Enable or disable built-in spell-check.

```csharp
RichEditBoxElement IsSpellCheckEnabled(this RichEditBoxElement el, bool enabled = true)
TextBoxElement IsSpellCheckEnabled(this TextBoxElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsSuggestionListOpen`

Programmatically open or close the suggestion list.

```csharp
AutoSuggestBoxElement IsSuggestionListOpen(this AutoSuggestBoxElement el, bool open = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `open` | `bool` | `true` |  |

## `IsTodayHighlighted`

Highlight today's date in the popup.

```csharp
CalendarDatePickerElement IsTodayHighlighted(this CalendarDatePickerElement el, bool highlighted = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `highlighted` | `bool` | `true` |  |

## `MaxDate`

Latest selectable date.

```csharp
CalendarViewElement MaxDate(this CalendarViewElement el, DateTimeOffset date)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `date` | `DateTimeOffset` |  |

## `MaxDropDownHeight`

Maximum pixel height of the open drop-down.

```csharp
ComboBoxElement MaxDropDownHeight(this ComboBoxElement el, double height)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

## `MaxLength`

Maximum number of characters allowed. `0` = no limit.

```csharp
PasswordBoxElement MaxLength(this PasswordBoxElement el, int maxLength)
RichEditBoxElement MaxLength(this RichEditBoxElement el, int maxLength)
TextBoxElement MaxLength(this TextBoxElement el, int maxLength)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `MaxLength(PasswordBoxElement, int)` | Maximum number of characters allowed. `0` = no limit. |
| `MaxLength(RichEditBoxElement, int)` | Maximum number of characters allowed. `0` = no limit. |
| `MaxLength(TextBoxElement, int)` | Maximum number of characters allowed in the box. Use `0` for no limit. |

**Applies to:** [`PasswordBoxElement`](../elements/input.md#passwordboxelement), [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `maxLength` | `int` |  |

## `MaxRating`

```csharp
RatingControlElement MaxRating(this RatingControlElement el, int max)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `max` | `int` |  |

## `MinDate`

Earliest selectable date.

```csharp
CalendarViewElement MinDate(this CalendarViewElement el, DateTimeOffset date)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `date` | `DateTimeOffset` |  |

## `MonthFormat`

Display format string for the month column.

```csharp
DatePickerElement MonthFormat(this DatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`DatePickerElement`](../elements/input.md#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

## `MoreButtonVisible`

Whether the "More" disclosure button is shown to expand additional inputs.

```csharp
ColorPickerElement MoreButtonVisible(this ColorPickerElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `NumberFormatter`

Sets a custom number formatter (currency, percent, scientific, etc.).

```csharp
NumberBoxElement NumberFormatter(this NumberBoxElement el, INumberFormatter2 formatter)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatter` | `INumberFormatter2` |  |

## `NumberOfWeeksInView`

How many week rows to display in month mode (2–8).

```csharp
CalendarViewElement NumberOfWeeksInView(this CalendarViewElement el, int weeks)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `weeks` | `int` |  |

## `NumericInput`

Sets `InputScope = Number`. Drives the soft-keyboard layout and IME hints on platforms that respect it.

```csharp
TextBoxElement NumericInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

## `Orientation`

Layout direction of the picker.

```csharp
DatePickerElement Orientation(this DatePickerElement el, Orientation orientation)
SliderElement Orientation(this SliderElement el, Orientation orientation)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Orientation(DatePickerElement, Orientation)` | Layout direction of the picker. |
| `Orientation(SliderElement, Orientation)` | Sets the slider orientation (horizontal or vertical). |

**Applies to:** [`DatePickerElement`](../elements/input.md#datepickerelement), [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `Orientation` |  |

## `PasswordChanged`

Wires the password-changed handler. Passing `null` clears.

```csharp
PasswordBoxElement PasswordChanged(this PasswordBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`PasswordBoxElement`](../elements/input.md#passwordboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `PasswordChar`

Character displayed in place of the entered password.

```csharp
PasswordBoxElement PasswordChar(this PasswordBoxElement el, string passwordChar)
```

Declared in `ElementExtensions`.

**Applies to:** [`PasswordBoxElement`](../elements/input.md#passwordboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `passwordChar` | `string` |  |

## `PasswordRevealMode`

Controls how the reveal button behaves.

```csharp
PasswordBoxElement PasswordRevealMode(this PasswordBoxElement el, PasswordRevealMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`PasswordBoxElement`](../elements/input.md#passwordboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `PasswordRevealMode` |  |

## `PhoneInput`

Sets `InputScope = TelephoneNumber`.

```csharp
TextBoxElement PhoneInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

## `PlaceholderText`

```csharp
AutoSuggestBoxElement PlaceholderText(this AutoSuggestBoxElement el, string text)
CalendarDatePickerElement PlaceholderText(this CalendarDatePickerElement el, string text)
ComboBoxElement PlaceholderText(this ComboBoxElement el, string text)
NumberBoxElement PlaceholderText(this NumberBoxElement el, string text)
PasswordBoxElement PlaceholderText(this PasswordBoxElement el, string text)
RichEditBoxElement PlaceholderText(this RichEditBoxElement el, string text)
TextBoxElement PlaceholderText(this TextBoxElement el, string text)
AutoSuggestElement<T> PlaceholderText<T>(this AutoSuggestElement<T> el, string text)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement), [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement), [`ComboBoxElement`](../elements/input.md#comboboxelement), [`NumberBoxElement`](../elements/input.md#numberboxelement), [`PasswordBoxElement`](../elements/input.md#passwordboxelement), [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`TextBoxElement`](../elements/input.md#textboxelement), [`AutoSuggestElement<T>`](../elements/input.md#autosuggestelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

## `PlaceholderValue`

Star value shown when the rating is unset. Use a negative number to disable the placeholder.

```csharp
RatingControlElement PlaceholderValue(this RatingControlElement el, double value)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

## `QueryIcon`

Icon rendered in the trailing query slot.

```csharp
AutoSuggestBoxElement QueryIcon(this AutoSuggestBoxElement el, IconData icon)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `icon` | [`IconData`](../controls/icons.md#icondata) |  |

## `QuerySubmitted`

Wires the query-submitted handler. Passing `null` clears.

```csharp
AutoSuggestBoxElement QuerySubmitted(this AutoSuggestBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `Range`

```csharp
NumberBoxElement Range(this NumberBoxElement el, double min, double max)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `min` | `double` |  |
| `max` | `double` |  |

## `ReadOnly`

Sets whether the RatingControl is read-only.

```csharp
RatingControlElement ReadOnly(this RatingControlElement el, bool readOnly = true)
TextBoxElement ReadOnly(this TextBoxElement el, bool readOnly = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsReadOnly() — aligns with WinUI naming (see #268).

| Overload | Description |
| --- | --- |
| `ReadOnly(RatingControlElement, bool)` | Sets whether the RatingControl is read-only. |
| `ReadOnly(TextBoxElement, bool)` | Sets whether the text field is read-only. |

**Applies to:** [`RatingControlElement`](../elements/input.md#ratingcontrolelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `readOnly` | `bool` | `true` |  |

## `SaturationRange`

Inclusive saturation bounds (0–100).

```csharp
ColorPickerElement SaturationRange(this ColorPickerElement el, int minSaturation, int maxSaturation)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minSaturation` | `int` |  |
| `maxSaturation` | `int` |  |

## `SearchInput`

Sets `InputScope = Search`.

```csharp
TextBoxElement SearchInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

## `Selected`

Wires the selected-item-changed handler for the typed auto-suggest. Passing `null` clears.

```csharp
AutoSuggestElement<T> Selected<T>(this AutoSuggestElement<T> el, Action<T?>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestElement<T>`](../elements/input.md#autosuggestelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<T?>?` |  |

## `SelectedDates`

Sets the initial multi-date selection. Re-renders with a different list reconcile the underlying selection via diff (echo events are suppressed).

```csharp
CalendarViewElement SelectedDates(this CalendarViewElement el, IReadOnlyList<DateTimeOffset>? dates)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dates` | `IReadOnlyList<DateTimeOffset>?` |  |

## `SelectedDatesChanged`

Wires the multi-date selection-changed handler. The callback receives a snapshot of the full selection (not just added/removed dates). Not raised on the initial declarative selection applied at mount. Passing `null` clears.

```csharp
CalendarViewElement SelectedDatesChanged(this CalendarViewElement el, Action<IReadOnlyList<DateTimeOffset>>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](../elements/input.md#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<IReadOnlyList<DateTimeOffset>>?` |  |

## `SnapsTo`

Whether the thumb snaps to ticks or step values during drag.

```csharp
SliderElement SnapsTo(this SliderElement el, SliderSnapsTo snapsTo)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `snapsTo` | `SliderSnapsTo` |  |

## `SpinButtons`

```csharp
NumberBoxElement SpinButtons(this NumberBoxElement el, NumberBoxSpinButtonPlacementMode placement = (NumberBoxSpinButtonPlacementMode)2)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `placement` | `NumberBoxSpinButtonPlacementMode` | `(NumberBoxSpinButtonPlacementMode)2` |  |

## `StepFrequency`

```csharp
SliderElement StepFrequency(this SliderElement el, double step)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `step` | `double` |  |

## `SuggestionChosen`

Wires the suggestion-chosen handler. Passing `null` clears. Spec §3.4: before this fluent the event was only reachable via property-initializer syntax.

```csharp
AutoSuggestBoxElement SuggestionChosen(this AutoSuggestBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `TextChanged`

Wires the text-changed handler. Passing `null` clears.

```csharp
AutoSuggestBoxElement TextChanged(this AutoSuggestBoxElement el, Action<string>? handler)
RichEditBoxElement TextChanged(this RichEditBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement), [`RichEditBoxElement`](../elements/input.md#richeditboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `ThumbToolTip`

Whether the floating value tooltip appears while dragging the thumb.

```csharp
SliderElement ThumbToolTip(this SliderElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `TickFrequency`

Sets the interval between tick marks. Pair with `.TickPlacement` to make ticks visible.

```csharp
SliderElement TickFrequency(this SliderElement el, double frequency)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `frequency` | `double` |  |

## `TickPlacement`

Controls where tick marks render relative to the slider track.

```csharp
SliderElement TickPlacement(this SliderElement el, TickPlacement placement)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `placement` | `TickPlacement` |  |

## `TimeChanged`

Wires the time-changed handler. Passing `null` clears.

```csharp
TimePickerElement TimeChanged(this TimePickerElement el, Action<TimeSpan>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TimePickerElement`](../elements/input.md#timepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TimeSpan>?` |  |

## `UrlInput`

Sets `InputScope = Url`.

```csharp
TextBoxElement UrlInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](../elements/input.md#textboxelement).

## `ValidationMode`

Controls how invalid input is handled on commit.

```csharp
NumberBoxElement ValidationMode(this NumberBoxElement el, NumberBoxValidationMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `NumberBoxValidationMode` |  |

## `ValueChanged`

Wires the value-changed handler. Passing `null` clears.

```csharp
NumberBoxElement ValueChanged(this NumberBoxElement el, Action<double>? handler)
RatingControlElement ValueChanged(this RatingControlElement el, Action<double>? handler)
SliderElement ValueChanged(this SliderElement el, Action<double>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `ValueChanged(NumberBoxElement, Action<double>?)` | Wires the value-changed handler. Passing `null` clears. |
| `ValueChanged(RatingControlElement, Action<double>?)` | Wires the rating-value-changed handler. Passing `null` clears. |
| `ValueChanged(SliderElement, Action<double>?)` | Wires the value-changed handler. Passing `null` clears. |

**Applies to:** [`NumberBoxElement`](../elements/input.md#numberboxelement), [`RatingControlElement`](../elements/input.md#ratingcontrolelement), [`SliderElement`](../elements/input.md#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<double>?` |  |

## `ValueRange`

Inclusive value/brightness bounds (0–100).

```csharp
ColorPickerElement ValueRange(this ColorPickerElement el, int minValue, int maxValue)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](../elements/input.md#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minValue` | `int` |  |
| `maxValue` | `int` |  |

## `YearFormat`

Display format string for the year column.

```csharp
DatePickerElement YearFormat(this DatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`DatePickerElement`](../elements/input.md#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |
