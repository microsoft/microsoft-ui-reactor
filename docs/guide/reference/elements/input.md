# Elements: Input

Text boxes, check boxes, combo boxes, sliders, pickers and other input controls.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Controls](../../controls.md), [Forms and Input](../../forms.md)

## `AutoSuggestBoxElement`

Auto-suggest box element. `Text` defaults to [`Optional.Unset`](../components/Optional.md#unset) so typed text is not overwritten unless explicitly set.

```csharp
public record AutoSuggestBoxElement(Optional<string> Text = default, Action<string>? OnTextChanged = null, Action<string>? OnQuerySubmitted = null, Action<string>? OnSuggestionChosen = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`AutoSuggestBox`](../factories/input.md#autosuggestbox)

**Modifiers:** [`Header`](../modifiers/input.md#header), [`IsSuggestionListOpen`](../modifiers/input.md#issuggestionlistopen), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`QueryIcon`](../modifiers/input.md#queryicon), [`QuerySubmitted`](../modifiers/input.md#querysubmitted), [`Set`](../modifiers/native-properties.md#set), [`SuggestionChosen`](../modifiers/input.md#suggestionchosen), [`TextChanged`](../modifiers/input.md#textchanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  | Optional label rendered above the box. |
| `IsSuggestionListOpen` | `bool` |  | Programmatically open or close the suggestion list. Defaults to `false`. |
| `OnQuerySubmitted` | `Action<string>?` | `null` |  |
| `OnSuggestionChosen` | `Action<string>?` | `null` |  |
| `OnTextChanged` | `Action<string>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |
| `QueryIcon` | [`IconData?`](../controls/icons.md#icondata) |  | Icon rendered in the trailing query slot (e.g. a Search symbol). |
| `Suggestions` | `string[]` |  |  |
| `Text` | [`Optional<string>`](../components/Optional.md) | `default` |  |

## `AutoSuggestElement<T>`

A type-safe auto-suggest element with async search, debounce, and custom templates.

```csharp
public sealed record AutoSuggestElement<T>(T? Selected, Action<T?>? OnSelected = null, Func<string, CancellationToken, Task<IReadOnlyList<T>>>? Search = null, Func<T, string>? DisplayText = null, string? PlaceholderText = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](Element.md).

**Created by:** [`AutoSuggest`](../factories/input.md#autosuggest)

**Modifiers:** [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`Selected`](../modifiers/input.md#selected)

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

## `CalendarDatePickerElement`

Calendar date picker element. `Date` defaults to [`Optional.Unset`](../components/Optional.md#unset); use an explicit `null` value to assert no date.

```csharp
public record CalendarDatePickerElement(Optional<DateTimeOffset?> Date = default, Action<DateTimeOffset?>? OnDateChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`CalendarDatePicker`](../factories/input.md#calendardatepicker)

**Modifiers:** [`DateChanged`](../modifiers/input.md#datechanged), [`DateFormat`](../modifiers/input.md#dateformat), [`IsCalendarOpen`](../modifiers/input.md#iscalendaropen), [`IsGroupLabelVisible`](../modifiers/input.md#isgrouplabelvisible), [`IsTodayHighlighted`](../modifiers/input.md#istodayhighlighted), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Date` | [`Optional<DateTimeOffset?>`](../components/Optional.md) | `default` |  |
| `DateFormat` | `string?` |  | Display format string for the picker's text (see WinUI `DateFormat` reference). |
| `Header` | `string?` |  |  |
| `IsCalendarOpen` | `bool` |  | Programmatically open or close the calendar popup. |
| `IsGroupLabelVisible` | `bool` |  | Show month/year group label headers in the popup. Defaults to `true`. |
| `IsTodayHighlighted` | `bool` |  | Highlight today's date in the popup. Defaults to `true` (matches WinUI). |
| `MaxDate` | `DateTimeOffset?` |  |  |
| `MinDate` | `DateTimeOffset?` |  |  |
| `OnDateChanged` | `Action<DateTimeOffset?>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |

## `CalendarViewElement`

The element record that describes a WinUI [`CalendarView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.calendarview) control.

```csharp
public record CalendarViewElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`CalendarView`](../factories/input.md#calendarview)

**Modifiers:** [`DisplayMode`](../modifiers/input.md#displaymode), [`FirstDayOfWeek`](../modifiers/input.md#firstdayofweek), [`MaxDate`](../modifiers/input.md#maxdate), [`MinDate`](../modifiers/input.md#mindate), [`NumberOfWeeksInView`](../modifiers/input.md#numberofweeksinview), [`SelectedDates`](../modifiers/input.md#selecteddates), [`SelectedDatesChanged`](../modifiers/input.md#selecteddateschanged), [`Set`](../modifiers/native-properties.md#set)

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

## `CheckBoxElement`

Check box element. `IsChecked` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its checked state until explicitly set.

```csharp
public record CheckBoxElement(Optional<bool?> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, string? Label = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`CheckBox`](../factories/input.md#checkbox), [`ThreeStateCheckBox`](../factories/input.md#threestatecheckbox)

**Modifiers:** [`CheckedStateChanged`](../modifiers/shared-controls.md#checkedstatechanged), [`IsCheckedChanged`](../modifiers/shared-controls.md#ischeckedchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `CheckedState` | `bool?` |  |  |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `IsChecked` | [`Optional<bool?>`](../components/Optional.md) | `default` |  |
| `IsThreeState` | `bool` |  |  |
| `Label` | `string?` | `null` |  |
| `OnCheckedStateChanged` | `Action<bool?>?` |  |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |

## `ColorPickerElement`

Color picker element. `Color` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its color until explicitly set.

```csharp
public record ColorPickerElement(Optional<Color> Color = default, Action<Color>? OnColorChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ColorPicker`](../factories/input.md#colorpicker)

**Modifiers:** [`AlphaEnabled`](../modifiers/input.md#alphaenabled), [`ColorChanged`](../modifiers/input.md#colorchanged), [`ColorChannelTextInputVisible`](../modifiers/input.md#colorchanneltextinputvisible), [`ColorSliderVisible`](../modifiers/input.md#colorslidervisible), [`ColorSpectrumShape`](../modifiers/input.md#colorspectrumshape), [`ColorSpectrumVisible`](../modifiers/input.md#colorspectrumvisible), [`HexInputVisible`](../modifiers/input.md#hexinputvisible), [`HueRange`](../modifiers/input.md#huerange), [`MoreButtonVisible`](../modifiers/input.md#morebuttonvisible), [`SaturationRange`](../modifiers/input.md#saturationrange), [`Set`](../modifiers/native-properties.md#set), [`ValueRange`](../modifiers/input.md#valuerange)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Color` | [`Optional<Color>`](../components/Optional.md) | `default` |  |
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

## `ComboBoxElement`

ComboBox element. `SelectedIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection.

```csharp
public record ComboBoxElement(string[] Items, Optional<int> SelectedIndex = default, Action<int>? OnSelectedIndexChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ComboBox`](../factories/input.md#combobox)

**Modifiers:** [`Description`](../modifiers/input.md#description), [`DropDownClosed`](../modifiers/input.md#dropdownclosed), [`DropDownOpened`](../modifiers/input.md#dropdownopened), [`Editable`](../modifiers/input.md#editable), [`Header`](../modifiers/input.md#header), [`IsEditable`](../modifiers/input.md#iseditable), [`MaxDropDownHeight`](../modifiers/input.md#maxdropdownheight), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Description` | `string?` |  | Help text rendered below the box. WinUI 3 1.2+ feature. |
| `Header` | `string?` |  |  |
| `IsEditable` | `bool` |  |  |
| `ItemElements` | [`Element[]?`](Element.md) |  |  |
| `Items` | `string[]` |  |  |
| `MaxDropDownHeight` | `double` |  | Maximum pixel height of the open drop-down. `NaN` (default) uses the WinUI default. |
| `OnDropDownClosed` | `Action?` |  | Raised when the drop-down list closes (either by selection or dismissal). |
| `OnDropDownOpened` | `Action?` |  | Raised when the user opens the drop-down list. |
| `OnSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `PlaceholderText` | `string?` |  |  |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |

## `DatePickerElement`

Date picker element. `Date` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its date until explicitly set.

```csharp
public record DatePickerElement(Optional<DateTimeOffset> Date = default, Action<DateTimeOffset>? OnDateChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`DatePicker`](../factories/input.md#datepicker)

**Modifiers:** [`DateChanged`](../modifiers/input.md#datechanged), [`DayFormat`](../modifiers/input.md#dayformat), [`MonthFormat`](../modifiers/input.md#monthformat), [`Orientation`](../modifiers/input.md#orientation), [`Set`](../modifiers/native-properties.md#set), [`YearFormat`](../modifiers/input.md#yearformat)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Date` | [`Optional<DateTimeOffset>`](../components/Optional.md) | `default` |  |
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

## `MaskedTextBoxElement`

A text box that enforces a mask pattern for structured input.

```csharp
public sealed record MaskedTextBoxElement(string Value, Action<string>? OnChanged = null, string? Mask = null, string? Header = null, char Placeholder = '_') : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](Element.md).

**Created by:** [`MaskedTextBox`](../factories/input.md#maskedtextbox)

**Modifiers:** [`Changed`](../modifiers/input.md#changed)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` | `null` |  |
| `IsComplete` | `bool` |  | Whether all required positions in the mask are filled. |
| `Mask` | `string?` | `null` |  |
| `OnChanged` | `Action<string>?` | `null` |  |
| `Placeholder` | `char` | `'_'` |  |
| `RawValue` | `string` |  | The raw value (without mask literals and placeholders). |
| `Value` | `string` |  |  |

## `NumberBoxElement`

Number box element. `Value` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its value until explicitly set.

```csharp
public record NumberBoxElement(Optional<double> Value = default, Action<double>? OnValueChanged = null, string? Header = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`NumberBox`](../factories/input.md#numberbox)

**Modifiers:** [`AcceptsExpression`](../modifiers/input.md#acceptsexpression), [`Description`](../modifiers/input.md#description), [`Focus`](../modifiers/input.md#focus), [`NumberFormatter`](../modifiers/input.md#numberformatter), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`Range`](../modifiers/input.md#range), [`Set`](../modifiers/native-properties.md#set), [`SpinButtons`](../modifiers/input.md#spinbuttons), [`ValidationMode`](../modifiers/input.md#validationmode), [`ValueChanged`](../modifiers/input.md#valuechanged)

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
| `Value` | [`Optional<double>`](../components/Optional.md) | `default` |  |

## `PasswordBoxElement`

Password box element. `Password` defaults to [`Optional.Unset`](../components/Optional.md#unset) so user input is not overwritten unless a value is explicitly provided.

```csharp
public record PasswordBoxElement(Optional<string> Password = default, Action<string>? OnPasswordChanged = null, string? PlaceholderText = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`PasswordBox`](../factories/input.md#passwordbox)

**Modifiers:** [`Header`](../modifiers/input.md#header), [`MaxLength`](../modifiers/input.md#maxlength), [`PasswordChanged`](../modifiers/input.md#passwordchanged), [`PasswordChar`](../modifiers/input.md#passwordchar), [`PasswordRevealMode`](../modifiers/input.md#passwordrevealmode), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  | Optional label rendered above the box. |
| `MaxLength` | `int` |  | Maximum number of characters allowed. `0` (default) = no limit. |
| `OnPasswordChanged` | `Action<string>?` | `null` |  |
| `Password` | [`Optional<string>`](../components/Optional.md) | `default` |  |
| `PasswordChar` | `string?` |  | Character displayed in place of the entered password (default '●' bullet). |
| `PasswordRevealMode` | `PasswordRevealMode` |  | How the reveal button behaves. Defaults to `Peek` (matches WinUI). |
| `PlaceholderText` | `string?` | `null` |  |

## `RadioButtonElement`

Radio button element. `IsChecked` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its checked state until explicitly set.

```csharp
public record RadioButtonElement(string Label, Optional<bool> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, string? GroupName = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RadioButton`](../factories/input.md#radiobutton)

**Modifiers:** [`IsCheckedChanged`](../modifiers/shared-controls.md#ischeckedchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ContentElement` | [`Element?`](Element.md) |  | Declarative content, taking precedence over the text value. |
| `GroupName` | `string?` | `null` |  |
| `IsChecked` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `Label` | `string` |  |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |

## `RadioButtonsElement`

RadioButtons element. `SelectedIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection.

```csharp
public record RadioButtonsElement(string[] Items, Optional<int> SelectedIndex = default, Action<int>? OnSelectedIndexChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RadioButtons`](../factories/input.md#radiobuttons)

**Modifiers:** [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  |  |
| `Items` | `string[]` |  |  |
| `OnSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |

## `RatingControlElement`

Rating control element. `Value` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its rating until explicitly set.

```csharp
public record RatingControlElement(Optional<double> Value = default, Action<double>? OnValueChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RatingControl`](../factories/input.md#ratingcontrol)

**Modifiers:** [`Caption`](../modifiers/input.md#caption), [`InitialSetValue`](../modifiers/input.md#initialsetvalue), [`IsReadOnly`](../modifiers/input.md#isreadonly), [`MaxRating`](../modifiers/input.md#maxrating), [`PlaceholderValue`](../modifiers/input.md#placeholdervalue), [`ReadOnly`](../modifiers/shared-controls.md#readonly), [`Set`](../modifiers/native-properties.md#set), [`ValueChanged`](../modifiers/input.md#valuechanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Caption` | `string?` |  |  |
| `InitialSetValue` | `int` |  | Integer rating to assume when the user first interacts. Defaults to 1. (WinUI's `InitialSetValue` is int.) |
| `IsReadOnly` | `bool` |  |  |
| `MaxRating` | `int` |  |  |
| `OnValueChanged` | `Action<double>?` | `null` |  |
| `PlaceholderValue` | `double` |  | Star value shown when the rating is unset. Defaults to -1 (no placeholder). |
| `Value` | [`Optional<double>`](../components/Optional.md) | `default` |  |

## `RichEditBoxElement`

Rich edit box element. `Text` defaults to [`Optional.Unset`](../components/Optional.md#unset) so user text is not overwritten unless explicitly set.

```csharp
public record RichEditBoxElement(Optional<string> Text = default) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RichEditBox`](../factories/input.md#richeditbox)

**Modifiers:** [`AcceptsReturn`](../modifiers/input.md#acceptsreturn), [`IsSpellCheckEnabled`](../modifiers/input.md#isspellcheckenabled), [`MaxLength`](../modifiers/input.md#maxlength), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`SelectionHighlightColor`](../modifiers/typography.md#selectionhighlightcolor), [`Set`](../modifiers/native-properties.md#set), [`TextChanged`](../modifiers/input.md#textchanged), [`TextWrapping`](../modifiers/typography.md#textwrapping)

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
| `Text` | [`Optional<string>`](../components/Optional.md) | `default` |  |
| `TextWrapping` | `TextWrapping` |  | How text wraps within the box. Defaults to `Wrap`. |

## `SliderElement`

Slider element. `Value` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its value until explicitly set.

```csharp
public record SliderElement(Optional<double> Value = default, double Min = 0, double Max = 100, Action<double>? OnValueChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Slider`](../factories/input.md#slider)

**Modifiers:** [`Header`](../modifiers/input.md#header), [`Orientation`](../modifiers/input.md#orientation), [`Set`](../modifiers/native-properties.md#set), [`SnapsTo`](../modifiers/input.md#snapsto), [`StepFrequency`](../modifiers/input.md#stepfrequency), [`ThumbToolTip`](../modifiers/input.md#thumbtooltip), [`TickFrequency`](../modifiers/input.md#tickfrequency), [`TickPlacement`](../modifiers/input.md#tickplacement), [`ValueChanged`](../modifiers/input.md#valuechanged)

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
| `Value` | [`Optional<double>`](../components/Optional.md) | `default` |  |

## `TextBoxElement`

Text box element. `Value` defaults to [`Optional.Unset`](../components/Optional.md#unset) so user text is not overwritten unless a value is explicitly provided.

```csharp
public record TextBoxElement(Optional<string> Value = default, Action<string>? OnChanged = null, string? PlaceholderText = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`TextBox`](../factories/input.md#textbox)

**Modifiers:** [`AcceptsReturn`](../modifiers/input.md#acceptsreturn), [`Changed`](../modifiers/input.md#changed), [`CharacterCasing`](../modifiers/input.md#charactercasing), [`Description`](../modifiers/input.md#description), [`EmailInput`](../modifiers/input.md#emailinput), [`Focus`](../modifiers/input.md#focus), [`Header`](../modifiers/input.md#header), [`InputScope`](../modifiers/input.md#inputscope), [`IsReadOnly`](../modifiers/input.md#isreadonly), [`IsSpellCheckEnabled`](../modifiers/input.md#isspellcheckenabled), [`MaxLength`](../modifiers/input.md#maxlength), [`NumericInput`](../modifiers/input.md#numericinput), [`PhoneInput`](../modifiers/input.md#phoneinput), [`PlaceholderText`](../modifiers/input.md#placeholdertext), [`ReadOnly`](../modifiers/shared-controls.md#readonly), [`SearchInput`](../modifiers/input.md#searchinput), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`Set`](../modifiers/native-properties.md#set), [`TextAlignment`](../modifiers/typography.md#textalignment), [`TextWrapping`](../modifiers/typography.md#textwrapping), [`UrlInput`](../modifiers/input.md#urlinput)

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
| `Value` | [`Optional<string>`](../components/Optional.md) | `default` |  |

## `TimePickerElement`

Time picker element. `Time` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its time until explicitly set.

```csharp
public record TimePickerElement(Optional<TimeSpan> Time = default, Action<TimeSpan>? OnTimeChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`TimePicker`](../factories/input.md#timepicker)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`TimeChanged`](../modifiers/input.md#timechanged)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ClockIdentifier` | `int` |  |  |
| `Header` | `string?` |  |  |
| `MinuteIncrement` | `int` |  |  |
| `OnTimeChanged` | `Action<TimeSpan>?` | `null` |  |
| `Time` | [`Optional<TimeSpan>`](../components/Optional.md) | `default` |  |

## `ToggleSwitchElement`

Toggle switch element. `IsOn` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its state until explicitly set.

```csharp
public record ToggleSwitchElement(Optional<bool> IsOn = default, Action<bool>? OnIsOnChanged = null, string? OnContent = null, string? OffContent = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ToggleSwitch`](../factories/input.md#toggleswitch)

**Modifiers:** [`Header`](../modifiers/input.md#header), [`IsOnChanged`](../modifiers/input.md#isonchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Header` | `string?` |  |  |
| `IsOn` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `OffContent` | `string?` | `null` |  |
| `OnContent` | `string?` | `null` |  |
| `OnIsOnChanged` | `Action<bool>?` | `null` |  |
