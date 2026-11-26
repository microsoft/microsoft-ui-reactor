---
title: "Elements: Input"
description: "Text boxes, check boxes, combo boxes, sliders, pickers and other input controls."
sidebar:
  label: "Input"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## `AutoSuggestBoxElement` [#autosuggestboxelement]

Auto-suggest box element. `Text` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so typed text is not overwritten unless explicitly set.

```csharp
public record AutoSuggestBoxElement(Optional<string> Text = default, Action<string>? OnTextChanged = null, Action<string>? OnQuerySubmitted = null, Action<string>? OnSuggestionChosen = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`AutoSuggestBox`](/microsoft-ui-reactor/main/reference/factories/input#autosuggestbox)

**Modifiers:** [`Header`](/microsoft-ui-reactor/main/reference/modifiers/input#header), [`IsSuggestionListOpen`](/microsoft-ui-reactor/main/reference/modifiers/input#issuggestionlistopen), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`QueryIcon`](/microsoft-ui-reactor/main/reference/modifiers/input#queryicon), [`QuerySubmitted`](/microsoft-ui-reactor/main/reference/modifiers/input#querysubmitted), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SuggestionChosen`](/microsoft-ui-reactor/main/reference/modifiers/input#suggestionchosen), [`TextChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#textchanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  | Optional label rendered above the box. |
| `IsSuggestionListOpen` | `bool` |  | Programmatically open or close the suggestion list. Defaults to `false`. |
| `OnQuerySubmitted` | `Action<string>?` | `null` |  |
| `OnSuggestionChosen` | `Action<string>?` | `null` |  |
| `OnTextChanged` | `Action<string>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |
| `QueryIcon` | [`IconData?`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) |  | Icon rendered in the trailing query slot (e.g. a Search symbol). |
| `Suggestions` | `string[]` |  |  |
| `Text` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `AutoSuggestElement<T>` [#autosuggestelementt]

A type-safe auto-suggest element with async search, debounce, and custom templates.

```csharp
public sealed record AutoSuggestElement<T>(T? Selected, Action<T?>? OnSelected = null, Func<string, CancellationToken, Task<IReadOnlyList<T>>>? Search = null, Func<T, string>? DisplayText = null, string? PlaceholderText = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`AutoSuggest`](/microsoft-ui-reactor/main/reference/factories/input#autosuggest)

**Modifiers:** [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`Selected`](/microsoft-ui-reactor/main/reference/modifiers/input#selected)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `DebounceMs` | `int` |  | Debounce delay in milliseconds before triggering search. Default: 300ms. |
| `DisplayText` | `Func<T, string>?` | `null` |  |
| `EmptyMessage` | `string` |  | Message to display when search returns no results. |
| `ErrorMessage` | `string` |  | Error message to display when search fails. |
| `OnSelected` | `Action<T?>?` | `null` |  |
| `PlaceholderText` | `string?` | `null` |  |
| `Search` | `Func<string, CancellationToken, Task<IReadOnlyList<T>>>?` | `null` |  |
| `Selected` | `T?` |  |  |
| `Template` | `Func<T, Element>?` |  | Custom template for rendering suggestion items. |

## `CalendarDatePickerElement` [#calendardatepickerelement]

Calendar date picker element. `Date` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use an explicit `null` value to assert no date.

```csharp
public record CalendarDatePickerElement(Optional<DateTimeOffset?> Date = default, Action<DateTimeOffset?>? OnDateChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`CalendarDatePicker`](/microsoft-ui-reactor/main/reference/factories/input#calendardatepicker)

**Modifiers:** [`DateChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#datechanged), [`DateFormat`](/microsoft-ui-reactor/main/reference/modifiers/input#dateformat), [`IsCalendarOpen`](/microsoft-ui-reactor/main/reference/modifiers/input#iscalendaropen), [`IsGroupLabelVisible`](/microsoft-ui-reactor/main/reference/modifiers/input#isgrouplabelvisible), [`IsTodayHighlighted`](/microsoft-ui-reactor/main/reference/modifiers/input#istodayhighlighted), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Date` | [`Optional<DateTimeOffset?>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `DateFormat` | `string?` |  | Display format string for the picker's text (see WinUI `DateFormat` reference). |
| `Header` | `string?` |  |  |
| `IsCalendarOpen` | `bool` |  | Programmatically open or close the calendar popup. |
| `IsGroupLabelVisible` | `bool` |  | Show month/year group label headers in the popup. Defaults to `true`. |
| `IsTodayHighlighted` | `bool` |  | Highlight today's date in the popup. Defaults to `true` (matches WinUI). |
| `MaxDate` | `DateTimeOffset?` |  |  |
| `MinDate` | `DateTimeOffset?` |  |  |
| `OnDateChanged` | `Action<DateTimeOffset?>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |

## `CalendarViewElement` [#calendarviewelement]

The element record that describes a WinUI [`CalendarView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.calendarview) control.

```csharp
public record CalendarViewElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`CalendarView`](/microsoft-ui-reactor/main/reference/factories/input#calendarview)

**Modifiers:** [`DisplayMode`](/microsoft-ui-reactor/main/reference/modifiers/input#displaymode), [`FirstDayOfWeek`](/microsoft-ui-reactor/main/reference/modifiers/input#firstdayofweek), [`MaxDate`](/microsoft-ui-reactor/main/reference/modifiers/input#maxdate), [`MinDate`](/microsoft-ui-reactor/main/reference/modifiers/input#mindate), [`NumberOfWeeksInView`](/microsoft-ui-reactor/main/reference/modifiers/input#numberofweeksinview), [`SelectedDates`](/microsoft-ui-reactor/main/reference/modifiers/input#selecteddates), [`SelectedDatesChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#selecteddateschanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `CalendarIdentifier` | `string?` |  |
| `DisplayMode` | `CalendarViewDisplayMode` | Initial display mode (Month / Year / Decade). Defaults to `Month`. |
| `FirstDayOfWeek` | `DayOfWeek?` | Day of the week that starts each row. `null` = locale default. |
| `IsGroupLabelVisible` | `bool` |  |
| `IsOutOfScopeEnabled` | `bool` |  |
| `Language` | `string?` |  |
| `MaxDate` | `DateTimeOffset?` | Latest selectable date. `null` = WinUI default (~100 years ahead). |
| `MinDate` | `DateTimeOffset?` | Earliest selectable date. `null` = WinUI default (~100 years back). |
| `NumberOfWeeksInView` | `int` | How many week rows to display in month mode (2–8). Defaults to 6. |
| `OnSelectedDatesChanged` | `Action<IReadOnlyList<DateTimeOffset>>?` | Raised when the user changes the selection. Receives a snapshot of the full selection (not just added/removed dates) — easier to bind into component state without diffing. Not raised on the initial declarative selection applied at mount. |
| `SelectedDates` | `IReadOnlyList<DateTimeOffset>?` | Initial selection. Bind for declarative selection on mount; subsequent programmatic updates re-apply only when the list reference differs. Combine with [`CalendarViewElement.OnSelectedDatesChanged`](#calendarviewelement) for two-way binding in multi-select mode. |
| `SelectionMode` | `CalendarViewSelectionMode` |  |

## `CheckBoxElement` [#checkboxelement]

Check box element. `IsChecked` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its checked state until explicitly set.

```csharp
public record CheckBoxElement(Optional<bool?> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, string? Label = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`CheckBox`](/microsoft-ui-reactor/main/reference/factories/input#checkbox), [`ThreeStateCheckBox`](/microsoft-ui-reactor/main/reference/factories/input#threestatecheckbox)

**Modifiers:** [`CheckedStateChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#checkedstatechanged), [`IsCheckedChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#ischeckedchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `CheckedState` | `bool?` |  |  |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `IsChecked` | [`Optional<bool?>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `IsThreeState` | `bool` |  |  |
| `Label` | `string?` | `null` |  |
| `OnCheckedStateChanged` | `Action<bool?>?` |  |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |

## `ColorPickerElement` [#colorpickerelement]

Color picker element. `Color` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its color until explicitly set.

```csharp
public record ColorPickerElement(Optional<Color> Color = default, Action<Color>? OnColorChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ColorPicker`](/microsoft-ui-reactor/main/reference/factories/input#colorpicker)

**Modifiers:** [`AlphaEnabled`](/microsoft-ui-reactor/main/reference/modifiers/input#alphaenabled), [`ColorChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#colorchanged), [`ColorChannelTextInputVisible`](/microsoft-ui-reactor/main/reference/modifiers/input#colorchanneltextinputvisible), [`ColorSliderVisible`](/microsoft-ui-reactor/main/reference/modifiers/input#colorslidervisible), [`ColorSpectrumShape`](/microsoft-ui-reactor/main/reference/modifiers/input#colorspectrumshape), [`ColorSpectrumVisible`](/microsoft-ui-reactor/main/reference/modifiers/input#colorspectrumvisible), [`HexInputVisible`](/microsoft-ui-reactor/main/reference/modifiers/input#hexinputvisible), [`HueRange`](/microsoft-ui-reactor/main/reference/modifiers/input#huerange), [`MoreButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/input#morebuttonvisible), [`SaturationRange`](/microsoft-ui-reactor/main/reference/modifiers/input#saturationrange), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`ValueRange`](/microsoft-ui-reactor/main/reference/modifiers/input#valuerange)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Color` | [`Optional<Color>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `ColorSpectrumShape` | `ColorSpectrumShape` |  | Shape of the 2D color spectrum (Box or Ring). Defaults to `Box`. |
| `IsAlphaEnabled` | `bool` |  |  |
| `IsColorChannelTextInputVisible` | `bool` |  |  |
| `IsColorSliderVisible` | `bool` |  |  |
| `IsColorSpectrumVisible` | `bool` |  |  |
| `IsHexInputVisible` | `bool` |  |  |
| `IsMoreButtonVisible` | `bool` |  |  |
| `MaxHue` | `int` |  | Maximum hue (0–359). Defaults to 359. |
| `MaxSaturation` | `int` |  | Maximum saturation (0–100). Defaults to 100. |
| `MaxValue` | `int` |  | Maximum value/brightness (0–100). Defaults to 100. |
| `MinHue` | `int` |  | Minimum hue (0–359). Defaults to 0. |
| `MinSaturation` | `int` |  | Minimum saturation (0–100). Defaults to 0. |
| `MinValue` | `int` |  | Minimum value/brightness (0–100). Defaults to 0. |
| `OnColorChanged` | `Action<Color>?` | `null` |  |

## `ComboBoxElement` [#comboboxelement]

ComboBox element. `SelectedIndex` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection.

```csharp
public record ComboBoxElement(string[] Items, Optional<int> SelectedIndex = default, Action<int>? OnSelectedIndexChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ComboBox`](/microsoft-ui-reactor/main/reference/factories/input#combobox)

**Modifiers:** [`Description`](/microsoft-ui-reactor/main/reference/modifiers/input#description), [`DropDownClosed`](/microsoft-ui-reactor/main/reference/modifiers/input#dropdownclosed), [`DropDownOpened`](/microsoft-ui-reactor/main/reference/modifiers/input#dropdownopened), [`Editable`](/microsoft-ui-reactor/main/reference/modifiers/input#editable), [`Header`](/microsoft-ui-reactor/main/reference/modifiers/input#header), [`IsEditable`](/microsoft-ui-reactor/main/reference/modifiers/input#iseditable), [`MaxDropDownHeight`](/microsoft-ui-reactor/main/reference/modifiers/input#maxdropdownheight), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Description` | `string?` |  | Help text rendered below the box. WinUI 3 1.2+ feature. |
| `Header` | `string?` |  |  |
| `IsEditable` | `bool` |  |  |
| `ItemElements` | [`Element[]?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `Items` | `string[]` |  |  |
| `MaxDropDownHeight` | `double` |  | Maximum pixel height of the open drop-down. `NaN` (default) uses the WinUI default. |
| `OnDropDownClosed` | `Action?` |  | Raised when the drop-down list closes (either by selection or dismissal). |
| `OnDropDownOpened` | `Action?` |  | Raised when the user opens the drop-down list. |
| `OnSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `DatePickerElement` [#datepickerelement]

Date picker element. `Date` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its date until explicitly set.

```csharp
public record DatePickerElement(Optional<DateTimeOffset> Date = default, Action<DateTimeOffset>? OnDateChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`DatePicker`](/microsoft-ui-reactor/main/reference/factories/input#datepicker)

**Modifiers:** [`DateChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#datechanged), [`DayFormat`](/microsoft-ui-reactor/main/reference/modifiers/input#dayformat), [`MonthFormat`](/microsoft-ui-reactor/main/reference/modifiers/input#monthformat), [`Orientation`](/microsoft-ui-reactor/main/reference/modifiers/input#orientation), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`YearFormat`](/microsoft-ui-reactor/main/reference/modifiers/input#yearformat)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Date` | [`Optional<DateTimeOffset>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `DayFormat` | `string?` |  | Display format string for the day column. `null` uses the WinUI default. |
| `DayVisible` | `bool` |  |  |
| `Header` | `string?` |  |  |
| `MaxYear` | `DateTimeOffset?` |  |  |
| `MinYear` | `DateTimeOffset?` |  |  |
| `MonthFormat` | `string?` |  | Display format string for the month column. `null` uses the WinUI default. |
| `MonthVisible` | `bool` |  |  |
| `OnDateChanged` | `Action<DateTimeOffset>?` | `null` |  |
| `Orientation` | `Orientation` |  | Layout direction of the picker. Defaults to `Horizontal`. |
| `YearFormat` | `string?` |  | Display format string for the year column. `null` uses the WinUI default. |
| `YearVisible` | `bool` |  |  |

## `MaskedTextBoxElement` [#maskedtextboxelement]

A text box that enforces a mask pattern for structured input.

```csharp
public sealed record MaskedTextBoxElement(string Value, Action<string>? OnChanged = null, string? Mask = null, string? Header = null, char Placeholder = '_') : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`MaskedTextBox`](/microsoft-ui-reactor/main/reference/factories/input#maskedtextbox)

**Modifiers:** [`Changed`](/microsoft-ui-reactor/main/reference/modifiers/input#changed)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` | `null` |  |
| `IsComplete` | `bool` |  | Whether all required positions in the mask are filled. |
| `Mask` | `string?` | `null` |  |
| `OnChanged` | `Action<string>?` | `null` |  |
| `Placeholder` | `char` | `'_'` |  |
| `RawValue` | `string` |  | The raw value (without mask literals and placeholders). |
| `Value` | `string` |  |  |

## `NumberBoxElement` [#numberboxelement]

Number box element. `Value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its value until explicitly set.

```csharp
public record NumberBoxElement(Optional<double> Value = default, Action<double>? OnValueChanged = null, string? Header = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`NumberBox`](/microsoft-ui-reactor/main/reference/factories/input#numberbox)

**Modifiers:** [`AcceptsExpression`](/microsoft-ui-reactor/main/reference/modifiers/input#acceptsexpression), [`Description`](/microsoft-ui-reactor/main/reference/modifiers/input#description), [`Focus`](/microsoft-ui-reactor/main/reference/modifiers/input#focus), [`NumberFormatter`](/microsoft-ui-reactor/main/reference/modifiers/input#numberformatter), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`Range`](/microsoft-ui-reactor/main/reference/modifiers/input#range), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SpinButtons`](/microsoft-ui-reactor/main/reference/modifiers/input#spinbuttons), [`ValidationMode`](/microsoft-ui-reactor/main/reference/modifiers/input#validationmode), [`ValueChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#valuechanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AcceptsExpression` | `bool` |  | Whether the user can type arithmetic expressions (e.g. `2*3+1`) that resolve on commit. |
| `Description` | `string?` |  | Help text rendered below the box. WinUI 3 1.2+ feature. |
| `Header` | `string?` | `null` |  |
| `LargeChange` | `double` |  |  |
| `Maximum` | `double` |  |  |
| `Minimum` | `double` |  |  |
| `NumberFormatter` | `INumberFormatter2?` |  | Custom number formatter (e.g. currency, percent). Null uses WinUI's default DecimalFormatter. |
| `OnValueChanged` | `Action<double>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |
| `SmallChange` | `double` |  |  |
| `SpinButtonPlacement` | `NumberBoxSpinButtonPlacementMode` |  |  |
| `ValidationMode` | `NumberBoxValidationMode` |  | How invalid input is treated. Defaults to `InvalidInputOverwritten` (matches WinUI default). |
| `Value` | [`Optional<double>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `PasswordBoxElement` [#passwordboxelement]

Password box element. `Password` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so user input is not overwritten unless a value is explicitly provided.

```csharp
public record PasswordBoxElement(Optional<string> Password = default, Action<string>? OnPasswordChanged = null, string? PlaceholderText = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`PasswordBox`](/microsoft-ui-reactor/main/reference/factories/input#passwordbox)

**Modifiers:** [`Header`](/microsoft-ui-reactor/main/reference/modifiers/input#header), [`MaxLength`](/microsoft-ui-reactor/main/reference/modifiers/input#maxlength), [`PasswordChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#passwordchanged), [`PasswordChar`](/microsoft-ui-reactor/main/reference/modifiers/input#passwordchar), [`PasswordRevealMode`](/microsoft-ui-reactor/main/reference/modifiers/input#passwordrevealmode), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  | Optional label rendered above the box. |
| `MaxLength` | `int` |  | Maximum number of characters allowed. `0` (default) = no limit. |
| `OnPasswordChanged` | `Action<string>?` | `null` |  |
| `Password` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `PasswordChar` | `string?` |  | Character displayed in place of the entered password (default '●' bullet). |
| `PasswordRevealMode` | `PasswordRevealMode` |  | How the reveal button behaves. Defaults to `Peek` (matches WinUI). |
| `PlaceholderText` | `string?` | `null` |  |

## `RadioButtonElement` [#radiobuttonelement]

Radio button element. `IsChecked` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its checked state until explicitly set.

```csharp
public record RadioButtonElement(string Label, Optional<bool> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, string? GroupName = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RadioButton`](/microsoft-ui-reactor/main/reference/factories/input#radiobutton)

**Modifiers:** [`IsCheckedChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#ischeckedchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ContentElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Declarative content, taking precedence over the text value. |
| `GroupName` | `string?` | `null` |  |
| `IsChecked` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `Label` | `string` |  |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |

## `RadioButtonsElement` [#radiobuttonselement]

RadioButtons element. `SelectedIndex` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection.

```csharp
public record RadioButtonsElement(string[] Items, Optional<int> SelectedIndex = default, Action<int>? OnSelectedIndexChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RadioButtons`](/microsoft-ui-reactor/main/reference/factories/input#radiobuttons)

**Modifiers:** [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  |  |
| `Items` | `string[]` |  |  |
| `OnSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `RatingControlElement` [#ratingcontrolelement]

Rating control element. `Value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its rating until explicitly set.

```csharp
public record RatingControlElement(Optional<double> Value = default, Action<double>? OnValueChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RatingControl`](/microsoft-ui-reactor/main/reference/factories/input#ratingcontrol)

**Modifiers:** [`Caption`](/microsoft-ui-reactor/main/reference/modifiers/input#caption), [`InitialSetValue`](/microsoft-ui-reactor/main/reference/modifiers/input#initialsetvalue), [`IsReadOnly`](/microsoft-ui-reactor/main/reference/modifiers/input#isreadonly), [`MaxRating`](/microsoft-ui-reactor/main/reference/modifiers/input#maxrating), [`PlaceholderValue`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdervalue), [`ReadOnly`](/microsoft-ui-reactor/main/reference/modifiers/input#readonly), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`ValueChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#valuechanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Caption` | `string?` |  |  |
| `InitialSetValue` | `int` |  | Integer rating to assume when the user first interacts. Defaults to 1. (WinUI's `InitialSetValue` is int.) |
| `IsReadOnly` | `bool` |  |  |
| `MaxRating` | `int` |  |  |
| `OnValueChanged` | `Action<double>?` | `null` |  |
| `PlaceholderValue` | `double` |  | Star value shown when the rating is unset. Defaults to -1 (no placeholder). |
| `Value` | [`Optional<double>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `RichEditBoxElement` [#richeditboxelement]

Rich edit box element. `Text` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so user text is not overwritten unless explicitly set.

```csharp
public record RichEditBoxElement(Optional<string> Text = default) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RichEditBox`](/microsoft-ui-reactor/main/reference/factories/input#richeditbox)

**Modifiers:** [`AcceptsReturn`](/microsoft-ui-reactor/main/reference/modifiers/input#acceptsreturn), [`IsSpellCheckEnabled`](/microsoft-ui-reactor/main/reference/modifiers/input#isspellcheckenabled), [`MaxLength`](/microsoft-ui-reactor/main/reference/modifiers/input#maxlength), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`SelectionHighlightColor`](/microsoft-ui-reactor/main/reference/modifiers/typography#selectionhighlightcolor), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TextChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#textchanged), [`TextWrapping`](/microsoft-ui-reactor/main/reference/modifiers/typography#textwrapping)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AcceptsReturn` | `bool` |  | Whether Enter inserts a newline (vs committing). Defaults to `true`. |
| `Header` | `string?` |  |  |
| `IsReadOnly` | `bool` |  |  |
| `IsSpellCheckEnabled` | `bool?` |  | Whether built-in spell-check is enabled. Defaults to the WinUI default (true). |
| `MaxLength` | `int` |  | Maximum number of characters allowed. `0` (default) = no limit. |
| `OnTextChanged` | `Action<string>?` |  |  |
| `PlaceholderText` | `string?` |  |  |
| `SelectionHighlightColor` | `SolidColorBrush?` |  | Brush used to render the selection highlight. `null` = WinUI default (accent). |
| `Text` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `TextWrapping` | `TextWrapping` |  | How text wraps within the box. Defaults to `Wrap`. |

## `SliderElement` [#sliderelement]

Slider element. `Value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its value until explicitly set.

```csharp
public record SliderElement(Optional<double> Value = default, double Min = 0, double Max = 100, Action<double>? OnValueChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Slider`](/microsoft-ui-reactor/main/reference/factories/input#slider)

**Modifiers:** [`Header`](/microsoft-ui-reactor/main/reference/modifiers/input#header), [`Orientation`](/microsoft-ui-reactor/main/reference/modifiers/input#orientation), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SnapsTo`](/microsoft-ui-reactor/main/reference/modifiers/input#snapsto), [`StepFrequency`](/microsoft-ui-reactor/main/reference/modifiers/input#stepfrequency), [`ThumbToolTip`](/microsoft-ui-reactor/main/reference/modifiers/input#thumbtooltip), [`TickFrequency`](/microsoft-ui-reactor/main/reference/modifiers/input#tickfrequency), [`TickPlacement`](/microsoft-ui-reactor/main/reference/modifiers/input#tickplacement), [`ValueChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#valuechanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  |  |
| `IsThumbToolTipEnabled` | `bool` |  | Whether the floating value tooltip appears while dragging the thumb. Defaults to `true`. |
| `Max` | `double` | `100` |  |
| `Min` | `double` | `0` |  |
| `OnValueChanged` | `Action<double>?` | `null` |  |
| `Orientation` | `Orientation` |  | Slider orientation. Defaults to `Orientation.Horizontal`. |
| `SnapsTo` | `SliderSnapsTo` |  | Whether the thumb snaps to ticks or step values during drag. |
| `StepFrequency` | `double` |  |  |
| `TickFrequency` | `double` |  | Interval between tick marks on the slider's track. Defaults to `0` (no ticks). |
| `TickPlacement` | `TickPlacement` |  | Where tick marks render relative to the track. Defaults to `TickPlacement.Inline`. |
| `Value` | [`Optional<double>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `TextBoxElement` [#textboxelement]

Text box element. `Value` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so user text is not overwritten unless a value is explicitly provided.

```csharp
public record TextBoxElement(Optional<string> Value = default, Action<string>? OnChanged = null, string? PlaceholderText = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`TextBox`](/microsoft-ui-reactor/main/reference/factories/input#textbox)

**Modifiers:** [`AcceptsReturn`](/microsoft-ui-reactor/main/reference/modifiers/input#acceptsreturn), [`Changed`](/microsoft-ui-reactor/main/reference/modifiers/input#changed), [`CharacterCasing`](/microsoft-ui-reactor/main/reference/modifiers/input#charactercasing), [`Description`](/microsoft-ui-reactor/main/reference/modifiers/input#description), [`EmailInput`](/microsoft-ui-reactor/main/reference/modifiers/input#emailinput), [`Focus`](/microsoft-ui-reactor/main/reference/modifiers/input#focus), [`Header`](/microsoft-ui-reactor/main/reference/modifiers/input#header), [`InputScope`](/microsoft-ui-reactor/main/reference/modifiers/input#inputscope), [`IsReadOnly`](/microsoft-ui-reactor/main/reference/modifiers/input#isreadonly), [`IsSpellCheckEnabled`](/microsoft-ui-reactor/main/reference/modifiers/input#isspellcheckenabled), [`MaxLength`](/microsoft-ui-reactor/main/reference/modifiers/input#maxlength), [`NumericInput`](/microsoft-ui-reactor/main/reference/modifiers/input#numericinput), [`PhoneInput`](/microsoft-ui-reactor/main/reference/modifiers/input#phoneinput), [`PlaceholderText`](/microsoft-ui-reactor/main/reference/modifiers/input#placeholdertext), [`ReadOnly`](/microsoft-ui-reactor/main/reference/modifiers/input#readonly), [`SearchInput`](/microsoft-ui-reactor/main/reference/modifiers/input#searchinput), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TextAlignment`](/microsoft-ui-reactor/main/reference/modifiers/typography#textalignment), [`TextWrapping`](/microsoft-ui-reactor/main/reference/modifiers/typography#textwrapping), [`UrlInput`](/microsoft-ui-reactor/main/reference/modifiers/input#urlinput)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AcceptsReturn` | `bool?` |  |  |
| `CharacterCasing` | `CharacterCasing` |  | Forces input to upper/lower-case as the user types. Defaults to `Normal` (no transform). |
| `Description` | `string?` |  | Help text rendered below the box. WinUI 3 1.2+ feature. |
| `Header` | `string?` |  |  |
| `IsReadOnly` | `bool?` |  |  |
| `IsSpellCheckEnabled` | `bool?` |  | Whether built-in spell-check is enabled. Defaults to the WinUI default (true). |
| `MaxLength` | `int` |  | Maximum number of characters allowed. `0` (default) means no limit. |
| `OnChanged` | `Action<string>?` | `null` |  |
| `OnSelectionChanged` | `Action<string, int, int>?` |  | Fires when the text selection changes. Receives (selectedText, selectionStart, selectionLength). |
| `PlaceholderText` | `string?` | `null` |  |
| `SelectionLength` | `int?` |  | Selection length. Set alongside SelectionStart to control the selection range. |
| `SelectionStart` | `int?` |  | Caret / selection start position. Set this to control where the caret sits after a text update. |
| `TextAlignment` | `TextAlignment` |  | Horizontal text alignment within the box. Defaults to `Left`. |
| `TextWrapping` | `TextWrapping?` |  |  |
| `Value` | [`Optional<string>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `TimePickerElement` [#timepickerelement]

Time picker element. `Time` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its time until explicitly set.

```csharp
public record TimePickerElement(Optional<TimeSpan> Time = default, Action<TimeSpan>? OnTimeChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`TimePicker`](/microsoft-ui-reactor/main/reference/factories/input#timepicker)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TimeChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#timechanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ClockIdentifier` | `int` |  |  |
| `Header` | `string?` |  |  |
| `MinuteIncrement` | `int` |  |  |
| `OnTimeChanged` | `Action<TimeSpan>?` | `null` |  |
| `Time` | [`Optional<TimeSpan>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |

## `ToggleSwitchElement` [#toggleswitchelement]

Toggle switch element. `IsOn` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) so the control owns its state until explicitly set.

```csharp
public record ToggleSwitchElement(Optional<bool> IsOn = default, Action<bool>? OnIsOnChanged = null, string? OnContent = null, string? OffContent = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ToggleSwitch`](/microsoft-ui-reactor/main/reference/factories/input#toggleswitch)

**Modifiers:** [`Header`](/microsoft-ui-reactor/main/reference/modifiers/input#header), [`IsOnChanged`](/microsoft-ui-reactor/main/reference/modifiers/input#isonchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  |  |
| `IsOn` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `OffContent` | `string?` | `null` |  |
| `OnContent` | `string?` | `null` |  |
| `OnIsOnChanged` | `Action<bool>?` | `null` |  |
