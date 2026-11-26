---
title: "Control modifiers: Input"
description: "Modifiers that only the controls in the Input area have."
sidebar:
  label: "Input"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## `AcceptsExpression` [#acceptsexpression]

Whether the user can type arithmetic expressions (e.g. `2*3+1`) that resolve on commit.

```csharp
NumberBoxElement AcceptsExpression(this NumberBoxElement el, bool accepts = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `accepts` | `bool` | `true` |  |

## `AcceptsReturn` [#acceptsreturn]

```csharp
RichEditBoxElement AcceptsReturn(this RichEditBoxElement el, bool accepts = true)
TextBoxElement AcceptsReturn(this TextBoxElement el, bool accepts = true)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `AcceptsReturn(RichEditBoxElement, bool)` | Whether Enter inserts a newline (vs committing input). |
| `AcceptsReturn(TextBoxElement, bool)` |  |

**Applies to:** [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `accepts` | `bool` | `true` |  |

## `AlphaEnabled` [#alphaenabled]

Whether the alpha (transparency) channel is editable.

```csharp
ColorPickerElement AlphaEnabled(this ColorPickerElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `Caption` [#caption]

Promotes the existing `Caption` init property to a fluent.

```csharp
RatingControlElement Caption(this RatingControlElement el, string caption)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `caption` | `string` |  |

## `Changed` [#changed]

```csharp
MaskedTextBoxElement Changed(this MaskedTextBoxElement el, Action<string>? handler)
TextBoxElement Changed(this TextBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Changed(MaskedTextBoxElement, Action<string>?)` | Wires the text-changed handler. Passing `null` clears. |
| `Changed(TextBoxElement, Action<string>?)` | Wires the text-changed handler. Receives the new text. Passing `null` clears. |

**Applies to:** [`MaskedTextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#maskedtextboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `CharacterCasing` [#charactercasing]

Forces input to upper/lower-case as the user types.

```csharp
TextBoxElement CharacterCasing(this TextBoxElement el, CharacterCasing casing)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `casing` | `CharacterCasing` |  |

## `ColorChanged` [#colorchanged]

Wires the color-changed handler. Passing `null` clears.

```csharp
ColorPickerElement ColorChanged(this ColorPickerElement el, Action<Color>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Color>?` |  |

## `ColorChannelTextInputVisible` [#colorchanneltextinputvisible]

Whether the per-channel numeric text inputs (RGB / HSV) are shown.

```csharp
ColorPickerElement ColorChannelTextInputVisible(this ColorPickerElement el, bool visible)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `ColorSliderVisible` [#colorslidervisible]

Whether the lightness/value slider is shown.

```csharp
ColorPickerElement ColorSliderVisible(this ColorPickerElement el, bool visible)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `ColorSpectrumShape` [#colorspectrumshape]

Shape of the 2D color spectrum (Box or Ring).

```csharp
ColorPickerElement ColorSpectrumShape(this ColorPickerElement el, ColorSpectrumShape shape)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `shape` | `ColorSpectrumShape` |  |

## `ColorSpectrumVisible` [#colorspectrumvisible]

Whether the 2D hue/saturation spectrum is shown.

```csharp
ColorPickerElement ColorSpectrumVisible(this ColorPickerElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `DateChanged` [#datechanged]

```csharp
CalendarDatePickerElement DateChanged(this CalendarDatePickerElement el, Action<DateTimeOffset?>? handler)
DatePickerElement DateChanged(this DatePickerElement el, Action<DateTimeOffset>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `DateChanged(CalendarDatePickerElement, Action<DateTimeOffset?>?)` | Wires the date-changed handler. Receives null when the user clears the selection. Passing `null` as the handler clears it. |
| `DateChanged(DatePickerElement, Action<DateTimeOffset>?)` | Wires the date-changed handler. Passing `null` clears. |

**Applies to:** [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement), [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DateTimeOffset?>?` or `Action<DateTimeOffset>?` |  |

## `DateFormat` [#dateformat]

Display format string for the picker's text. See WinUI's `DateFormat` reference.

```csharp
CalendarDatePickerElement DateFormat(this CalendarDatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

## `DayFormat` [#dayformat]

Display format string for the day column.

```csharp
DatePickerElement DayFormat(this DatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

## `Description` [#description]

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

**Applies to:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement), [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

## `DisplayMode` [#displaymode]

Initial display mode (Month / Year / Decade).

```csharp
CalendarViewElement DisplayMode(this CalendarViewElement el, CalendarViewDisplayMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `CalendarViewDisplayMode` |  |

## `DropDownClosed` [#dropdownclosed]

Wires the drop-down-closed handler (fires on dismissal or selection). Passing `null` clears.

```csharp
ComboBoxElement DropDownClosed(this ComboBoxElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `DropDownOpened` [#dropdownopened]

Wires the drop-down-opened handler. Passing `null` clears.

```csharp
ComboBoxElement DropDownOpened(this ComboBoxElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `Editable` [#editable]

Sets whether the ComboBox is editable.

```csharp
ComboBoxElement Editable(this ComboBoxElement el, bool editable = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsEditable() — aligns with WinUI naming (see #268).

**Applies to:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `editable` | `bool` | `true` |  |

## `EmailInput` [#emailinput]

Sets `InputScope = EmailSmtpAddress`.

```csharp
TextBoxElement EmailInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

## `FirstDayOfWeek` [#firstdayofweek]

Day of the week that starts each row.

```csharp
CalendarViewElement FirstDayOfWeek(this CalendarViewElement el, DayOfWeek day)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `day` | `DayOfWeek` |  |

## `Focus` [#focus]

```csharp
NumberBoxElement Focus(this NumberBoxElement el, FocusManager fm, string fieldName, bool autoFocus = false)
TextBoxElement Focus(this TextBoxElement el, FocusManager fm, string fieldName, bool autoFocus = false)
```

Declared in `FocusExtensions`.

| Overload | Description |
| --- | --- |
| `Focus(NumberBoxElement, FocusManager, string, bool)` | Registers this NumberBox with a FocusManager for programmatic focus control. |
| `Focus(TextBoxElement, FocusManager, string, bool)` | Registers this TextBox with a FocusManager for programmatic focus control. |

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fm` | [`FocusManager`](/microsoft-ui-reactor/main/reference/hooks/focus-helpers#focusmanager) |  |  |
| `fieldName` | `string` |  |  |
| `autoFocus` | `bool` | `false` |  |

## `Header` [#header]

```csharp
AutoSuggestBoxElement Header(this AutoSuggestBoxElement el, string header)
ComboBoxElement Header(this ComboBoxElement el, string header)
PasswordBoxElement Header(this PasswordBoxElement el, string header)
SliderElement Header(this SliderElement el, string header)
TextBoxElement Header(this TextBoxElement el, string header)
ToggleSwitchElement Header(this ToggleSwitchElement el, string header)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Header(AutoSuggestBoxElement, string)` | Optional label rendered above the box. |
| `Header(ComboBoxElement, string)` |  |
| `Header(PasswordBoxElement, string)` | Optional label rendered above the box. |
| `Header(SliderElement, string)` |  |
| `Header(TextBoxElement, string)` |  |
| `Header(ToggleSwitchElement, string)` |  |

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement), [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement), [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement), [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement), [`ToggleSwitchElement`](/microsoft-ui-reactor/main/reference/elements/input#toggleswitchelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | `string` |  |

## `HexInputVisible` [#hexinputvisible]

Whether the hex code text input is shown.

```csharp
ColorPickerElement HexInputVisible(this ColorPickerElement el, bool visible)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `HueRange` [#huerange]

Inclusive hue bounds (0–359).

```csharp
ColorPickerElement HueRange(this ColorPickerElement el, int minHue, int maxHue)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minHue` | `int` |  |
| `maxHue` | `int` |  |

## `InitialSetValue` [#initialsetvalue]

Integer rating to assume when the user first interacts.

```csharp
RatingControlElement InitialSetValue(this RatingControlElement el, int value)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `int` |  |

## `InputScope` [#inputscope]

Sets a specific [`InputScopeNameValue`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.inputscopenamevalue). Escape hatch for input scopes outside the named helpers above (e.g. `Chat`, `FormulaNumber`, `AlphanumericFullWidth`).

```csharp
TextBoxElement InputScope(this TextBoxElement el, InputScopeNameValue scope)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `scope` | `InputScopeNameValue` |  |

## `IsCalendarOpen` [#iscalendaropen]

Programmatically open or close the calendar popup.

```csharp
CalendarDatePickerElement IsCalendarOpen(this CalendarDatePickerElement el, bool open = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `open` | `bool` | `true` |  |

## `IsEditable` [#iseditable]

Sets whether the ComboBox is editable.

```csharp
ComboBoxElement IsEditable(this ComboBoxElement el, bool editable = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `editable` | `bool` | `true` |  |

## `IsGroupLabelVisible` [#isgrouplabelvisible]

Show month/year group label headers in the popup.

```csharp
CalendarDatePickerElement IsGroupLabelVisible(this CalendarDatePickerElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `IsOnChanged` [#isonchanged]

Wires the on/off-state-changed handler. Passing `null` clears.

```csharp
ToggleSwitchElement IsOnChanged(this ToggleSwitchElement el, Action<bool>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ToggleSwitchElement`](/microsoft-ui-reactor/main/reference/elements/input#toggleswitchelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<bool>?` |  |

## `IsReadOnly` [#isreadonly]

```csharp
RatingControlElement IsReadOnly(this RatingControlElement el, bool readOnly = true)
TextBoxElement IsReadOnly(this TextBoxElement el, bool readOnly = true)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `IsReadOnly(RatingControlElement, bool)` | Sets whether the RatingControl is read-only. |
| `IsReadOnly(TextBoxElement, bool)` | Sets whether the text field is read-only. |

**Applies to:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `readOnly` | `bool` | `true` |  |

## `IsSpellCheckEnabled` [#isspellcheckenabled]

Enable or disable built-in spell-check.

```csharp
RichEditBoxElement IsSpellCheckEnabled(this RichEditBoxElement el, bool enabled = true)
TextBoxElement IsSpellCheckEnabled(this TextBoxElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsSuggestionListOpen` [#issuggestionlistopen]

Programmatically open or close the suggestion list.

```csharp
AutoSuggestBoxElement IsSuggestionListOpen(this AutoSuggestBoxElement el, bool open = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `open` | `bool` | `true` |  |

## `IsTodayHighlighted` [#istodayhighlighted]

Highlight today's date in the popup.

```csharp
CalendarDatePickerElement IsTodayHighlighted(this CalendarDatePickerElement el, bool highlighted = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `highlighted` | `bool` | `true` |  |

## `MaxDate` [#maxdate]

Latest selectable date.

```csharp
CalendarViewElement MaxDate(this CalendarViewElement el, DateTimeOffset date)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `date` | `DateTimeOffset` |  |

## `MaxDropDownHeight` [#maxdropdownheight]

Maximum pixel height of the open drop-down.

```csharp
ComboBoxElement MaxDropDownHeight(this ComboBoxElement el, double height)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

## `MaxLength` [#maxlength]

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

**Applies to:** [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement), [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `maxLength` | `int` |  |

## `MaxRating` [#maxrating]

```csharp
RatingControlElement MaxRating(this RatingControlElement el, int max)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `max` | `int` |  |

## `MinDate` [#mindate]

Earliest selectable date.

```csharp
CalendarViewElement MinDate(this CalendarViewElement el, DateTimeOffset date)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `date` | `DateTimeOffset` |  |

## `MonthFormat` [#monthformat]

Display format string for the month column.

```csharp
DatePickerElement MonthFormat(this DatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |

## `MoreButtonVisible` [#morebuttonvisible]

Whether the "More" disclosure button is shown to expand additional inputs.

```csharp
ColorPickerElement MoreButtonVisible(this ColorPickerElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `NumberFormatter` [#numberformatter]

Sets a custom number formatter (currency, percent, scientific, etc.).

```csharp
NumberBoxElement NumberFormatter(this NumberBoxElement el, INumberFormatter2 formatter)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatter` | `INumberFormatter2` |  |

## `NumberOfWeeksInView` [#numberofweeksinview]

How many week rows to display in month mode (2–8).

```csharp
CalendarViewElement NumberOfWeeksInView(this CalendarViewElement el, int weeks)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `weeks` | `int` |  |

## `NumericInput` [#numericinput]

Sets `InputScope = Number`. Drives the soft-keyboard layout and IME hints on platforms that respect it.

```csharp
TextBoxElement NumericInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

## `Orientation` [#orientation]

```csharp
DatePickerElement Orientation(this DatePickerElement el, Orientation orientation)
SliderElement Orientation(this SliderElement el, Orientation orientation)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Orientation(DatePickerElement, Orientation)` | Layout direction of the picker. |
| `Orientation(SliderElement, Orientation)` | Sets the slider orientation (horizontal or vertical). |

**Applies to:** [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement), [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `Orientation` |  |

## `PasswordChanged` [#passwordchanged]

Wires the password-changed handler. Passing `null` clears.

```csharp
PasswordBoxElement PasswordChanged(this PasswordBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `PasswordChar` [#passwordchar]

Character displayed in place of the entered password.

```csharp
PasswordBoxElement PasswordChar(this PasswordBoxElement el, string passwordChar)
```

Declared in `ElementExtensions`.

**Applies to:** [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `passwordChar` | `string` |  |

## `PasswordRevealMode` [#passwordrevealmode]

Controls how the reveal button behaves.

```csharp
PasswordBoxElement PasswordRevealMode(this PasswordBoxElement el, PasswordRevealMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `PasswordRevealMode` |  |

## `PhoneInput` [#phoneinput]

Sets `InputScope = TelephoneNumber`.

```csharp
TextBoxElement PhoneInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

## `PlaceholderText` [#placeholdertext]

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

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement), [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement), [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement), [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement), [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement), [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement), [`AutoSuggestElement<T>`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

## `PlaceholderValue` [#placeholdervalue]

Star value shown when the rating is unset. Use a negative number to disable the placeholder.

```csharp
RatingControlElement PlaceholderValue(this RatingControlElement el, double value)
```

Declared in `ElementExtensions`.

**Applies to:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

## `QueryIcon` [#queryicon]

Icon rendered in the trailing query slot.

```csharp
AutoSuggestBoxElement QueryIcon(this AutoSuggestBoxElement el, IconData icon)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `icon` | [`IconData`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) |  |

## `QuerySubmitted` [#querysubmitted]

Wires the query-submitted handler. Passing `null` clears.

```csharp
AutoSuggestBoxElement QuerySubmitted(this AutoSuggestBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `Range` [#range]

```csharp
NumberBoxElement Range(this NumberBoxElement el, double min, double max)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `min` | `double` |  |
| `max` | `double` |  |

## `ReadOnly` [#readonly]

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

**Applies to:** [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `readOnly` | `bool` | `true` |  |

## `SaturationRange` [#saturationrange]

Inclusive saturation bounds (0–100).

```csharp
ColorPickerElement SaturationRange(this ColorPickerElement el, int minSaturation, int maxSaturation)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minSaturation` | `int` |  |
| `maxSaturation` | `int` |  |

## `SearchInput` [#searchinput]

Sets `InputScope = Search`.

```csharp
TextBoxElement SearchInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

## `Selected` [#selected]

Wires the selected-item-changed handler for the typed auto-suggest. Passing `null` clears.

```csharp
AutoSuggestElement<T> Selected<T>(this AutoSuggestElement<T> el, Action<T?>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestElement<T>`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<T?>?` |  |

## `SelectedDates` [#selecteddates]

Sets the initial multi-date selection. Re-renders with a different list reconcile the underlying selection via diff (echo events are suppressed).

```csharp
CalendarViewElement SelectedDates(this CalendarViewElement el, IReadOnlyList<DateTimeOffset>? dates)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dates` | `IReadOnlyList<DateTimeOffset>?` |  |

## `SelectedDatesChanged` [#selecteddateschanged]

Wires the multi-date selection-changed handler. The callback receives a snapshot of the full selection (not just added/removed dates). Not raised on the initial declarative selection applied at mount. Passing `null` clears.

```csharp
CalendarViewElement SelectedDatesChanged(this CalendarViewElement el, Action<IReadOnlyList<DateTimeOffset>>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<IReadOnlyList<DateTimeOffset>>?` |  |

## `SnapsTo` [#snapsto]

Whether the thumb snaps to ticks or step values during drag.

```csharp
SliderElement SnapsTo(this SliderElement el, SliderSnapsTo snapsTo)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `snapsTo` | `SliderSnapsTo` |  |

## `SpinButtons` [#spinbuttons]

```csharp
NumberBoxElement SpinButtons(this NumberBoxElement el, NumberBoxSpinButtonPlacementMode placement = (NumberBoxSpinButtonPlacementMode)2)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `placement` | `NumberBoxSpinButtonPlacementMode` | `(NumberBoxSpinButtonPlacementMode)2` |  |

## `StepFrequency` [#stepfrequency]

```csharp
SliderElement StepFrequency(this SliderElement el, double step)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `step` | `double` |  |

## `SuggestionChosen` [#suggestionchosen]

Wires the suggestion-chosen handler. Passing `null` clears. Spec §3.4: before this fluent the event was only reachable via property-initializer syntax.

```csharp
AutoSuggestBoxElement SuggestionChosen(this AutoSuggestBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `TextChanged` [#textchanged]

Wires the text-changed handler. Passing `null` clears.

```csharp
AutoSuggestBoxElement TextChanged(this AutoSuggestBoxElement el, Action<string>? handler)
RichEditBoxElement TextChanged(this RichEditBoxElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement), [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `ThumbToolTip` [#thumbtooltip]

Whether the floating value tooltip appears while dragging the thumb.

```csharp
SliderElement ThumbToolTip(this SliderElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `TickFrequency` [#tickfrequency]

Sets the interval between tick marks. Pair with `.TickPlacement` to make ticks visible.

```csharp
SliderElement TickFrequency(this SliderElement el, double frequency)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `frequency` | `double` |  |

## `TickPlacement` [#tickplacement]

Controls where tick marks render relative to the slider track.

```csharp
SliderElement TickPlacement(this SliderElement el, TickPlacement placement)
```

Declared in `ElementExtensions`.

**Applies to:** [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `placement` | `TickPlacement` |  |

## `TimeChanged` [#timechanged]

Wires the time-changed handler. Passing `null` clears.

```csharp
TimePickerElement TimeChanged(this TimePickerElement el, Action<TimeSpan>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TimePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#timepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TimeSpan>?` |  |

## `UrlInput` [#urlinput]

Sets `InputScope = Url`.

```csharp
TextBoxElement UrlInput(this TextBoxElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

## `ValidationMode` [#validationmode]

Controls how invalid input is handled on commit.

```csharp
NumberBoxElement ValidationMode(this NumberBoxElement el, NumberBoxValidationMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `NumberBoxValidationMode` |  |

## `ValueChanged` [#valuechanged]

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

**Applies to:** [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement), [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement), [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<double>?` |  |

## `ValueRange` [#valuerange]

Inclusive value/brightness bounds (0–100).

```csharp
ColorPickerElement ValueRange(this ColorPickerElement el, int minValue, int maxValue)
```

Declared in `ElementExtensions`.

**Applies to:** [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minValue` | `int` |  |
| `maxValue` | `int` |  |

## `YearFormat` [#yearformat]

Display format string for the year column.

```csharp
DatePickerElement YearFormat(this DatePickerElement el, string format)
```

Declared in `ElementExtensions`.

**Applies to:** [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `string` |  |
