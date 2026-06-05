
# Migration: `Optional<T>` controlled props

Spec 050 changes controlled element-record properties from plain `T` to
`Optional<T>`. Factory calls such as `TextBox(text, setText)`,
`Slider(value, 0, 100, setValue)`, and `ComboBox(items, index,
setIndex)` keep their existing plain-value signatures; they wrap the
value in `Optional.Of(value)` internally. Most app code therefore keeps
compiling unchanged.

The break you may see is direct element-record readback:

```csharp
// Before
int index = element.SelectedIndex;

// After: choose the intent
int index = element.SelectedIndex.GetValueOrDefault(-1); // tolerate control-owned
int asserted = element.SelectedIndex.Value;              // require HasValue
```

Use `Optional<T>.Unset` when the native control should own the value.
Use `Optional.Of(value)` when Reactor should force-assert the value on
Mount and Update.

## Migrated properties

| Element record | Property | New type |
|---|---|---|
| `CalendarDatePickerElement` | `Date` | `Optional<DateTimeOffset?>` |
| `CheckBoxElement` | `IsChecked` | `Optional<bool?>` |
| `ColorPickerElement` | `Color` | `Optional<Color>` |
| `DatePickerElement` | `Date` | `Optional<DateTimeOffset>` |
| `RadioButtonElement` | `IsChecked` | `Optional<bool>` |
| `RatingControlElement` | `Value` | `Optional<double>` |
| `SliderElement` | `Value` | `Optional<double>` |
| `TimePickerElement` | `Time` | `Optional<TimeSpan>` |
| `ToggleSplitButtonElement` | `IsChecked` | `Optional<bool>` |
| `ToggleSwitchElement` | `IsOn` | `Optional<bool>` |
| `AutoSuggestBoxElement` | `Text` | `Optional<string>` |
| `ComboBoxElement` | `SelectedIndex` | `Optional<int>` |
| `ExpanderElement` | `IsExpanded` | `Optional<bool>` |
| `FlipViewElement` | `SelectedIndex` | `Optional<int>` |
| `GridViewElement` | `SelectedIndex` | `Optional<int>` |
| `ListBoxElement` | `SelectedIndex` | `Optional<int>` |
| `ListViewElement` | `SelectedIndex` | `Optional<int>` |
| `NumberBoxElement` | `Value` | `Optional<double>` |
| `PasswordBoxElement` | `Password` | `Optional<string>` |
| `PipsPagerElement` | `SelectedPageIndex` | `Optional<int>` |
| `PivotElement` | `SelectedIndex` | `Optional<int>` |
| `RadioButtonsElement` | `SelectedIndex` | `Optional<int>` |
| `RichEditBoxElement` | `Text` | `Optional<string>` |
| `SelectorBarElement` | `SelectedIndex` | `Optional<int>` |
| `TabViewElement` | `SelectedIndex` | `Optional<int>` |
| `TemplatedFlipViewElement<T>` | `SelectedIndex` | `Optional<int>` |
| `TextBoxElement` | `Value` | `Optional<string>` |

## Sentinel-value migration

| Old pattern | If the control should own the value | If Reactor should force-assert |
|---|---|---|
| `SelectedIndex = -1` | `SelectedIndex = Optional<int>.Unset` | `SelectedIndex = Optional.Of(-1)` |
| `SelectedPageIndex = 0` as "not set" | `SelectedPageIndex = Optional<int>.Unset` | `SelectedPageIndex = Optional.Of(0)` |
| `IsChecked = false` as "not set" | `IsChecked = Optional<bool>.Unset` | `IsChecked = Optional.Of(false)` |
| `IsChecked = null` on tri-state checkbox | `IsChecked = Optional<bool?>.Unset` | `IsChecked = Optional.Of<bool?>(null)` |
| `Value = 0.0` as "not set" | `Value = Optional<double>.Unset` | `Value = Optional.Of(0.0)` |
| `Value = ""` / `Text = ""` as "not set" | `Value = Optional<string>.Unset` / `Text = Optional<string>.Unset` | `Optional.Of("")` |
| `Date = null` on `CalendarDatePicker` | `Date = Optional<DateTimeOffset?>.Unset` | `Date = Optional.Of<DateTimeOffset?>(null)` |

Reference-type null is explicit. For an `Optional<Brush>` property,
`with { Background = null }` means `Optional.Of(null)`, not `Unset`.
Write `Optional<Brush>.Unset` when you want WinUI fallback.

## Descriptor-author reminder

Custom descriptors must return `Optional<T>` from `.Controlled` and
`.HandCodedControlled` getters. For DP-backed one-way fallback, prefer
`.OneWay(get, set, dp:)`; analyzer `REACTOR0050` warns when an
`Optional<T>` OneWay entry omits the `dp:` ClearValue channel.

## Tips

**Prefer factories in app code.** `TextBox(text, setText)`, `Slider(value, ...)`, and selection factories keep plain-value overloads for the common state-bound path.

**Choose `Unset` intentionally.** Use `Optional<T>.Unset` only when the native WinUI control should own the value through user input, styling, template fallback, or default state.

**Force-assert sentinels explicitly.** If `-1`, `false`, `null`, or `""` is the value you want Reactor to assert, wrap it with `Optional.Of(...)` so future readers do not mistake it for "not set".

## Next Steps

- **[Extending Reactor controls](../extending-reactor-controls.md)** — Descriptor-author decision tree for `Controlled`, `OneWay(dp:)`, and `InitialOnly`.
- **[Advanced Patterns](../advanced.md#optionalt-and-authority)** — Snap-back and `ClearValue` recipes.
- **[Control Reconciler Protocol](../control-reconciler-protocol.md#optional-aware-update-gate)** — Runtime gate semantics for `Unset` and `HasValue`.
