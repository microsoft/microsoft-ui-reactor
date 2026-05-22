# Property & Event API Scrub — Control-by-Control Audit

## Status: Implemented

Phases 0–8 of the implementation landed on branch
`feat/039-property-event-scrub`; Phase 9 (docs) is landing in the same
branch. The task list with the per-section breakdown and tracker lives at
[`docs/specs/tasks/039-property-and-event-scrub-implementation.md`](tasks/039-property-and-event-scrub-implementation.md).

Phase 0–8 commit history: `git log feat/039-property-event-scrub --grep='Phase '`.

This spec catalogs every control Microsoft.UI.Reactor (Reactor) currently exposes through
`Microsoft.UI.Reactor.Factories` and audits each against three criteria.

### Decisions locked (from §15 open questions — final, will not change mid-implementation)

- **Q1 (source-gen vs hand-written):** Hand-written, in
  `src/Reactor/Elements/ElementExtensions.Events.cs`. A source generator was
  considered but rejected to keep build infrastructure simple; the shapes are
  regular enough that a self-test in `tests/Reactor.SelfTests/` enforces
  parity between callback properties and fluent extensions.

  **C# naming constraint (discovered during implementation):** The spec's
  literal `.OnClick(handler)` API cannot coexist with a delegate-typed
  property of the same name. C# binds `el.OnClick(arg)` to property-as-
  delegate invocation (`Action?.Invoke(arg)`) and never falls back to
  extension methods — see [reactor3 issue #39](https://example.invalid). The
  fluent extensions therefore drop the `On` prefix and match the WinUI XAML
  event-name convention:
  - Property `OnClick` → Fluent `.Click(handler)`
  - Property `OnTextChanged` → Fluent `.TextChanged(handler)`
  - Property `OnSelectedIndexChanged` → Fluent `.SelectedIndexChanged(handler)`
  - …and so on across the inventory.

  This deviates from the spec's literal §0.1 wording but preserves the goal
  (every callback property is reachable via a fluent extension). The
  property names are unchanged, so existing property-init syntax
  (`new ButtonElement(…) { OnClick = … }`) continues to compile.
- **Q2 (null semantics):** `.OnX(null)` clears any previously-set handler.
  No separate `.WithoutOnX()` helper.
- **Q3 (button styles):** `.AccentButton()` / `.SubtleButton()` fluent
  extensions land in `ElementExtensions.NamedStyles.cs`.
- **Q4 (CalendarView multi-select):** Distinct API for `CalendarView` —
  `Action<IReadOnlyList<DateTimeOffset>>? OnSelectedDatesChanged` plus an
  `IReadOnlyList<DateTimeOffset>? SelectedDates` init property.
- **Q5 (HyperlinkButton.NavigateUri):** Add the fluent `.NavigateUri(Uri)`
  extension; the existing XML doc comment promise is fulfilled.

## Goals

For every Reactor control surface, answer three questions:

1. **Event parity.** Every event that can be passed as a factory positional/named
   argument should *also* be configurable via a fluent modifier extension
   (e.g. `.OnClick(handler)`), so users who construct elements via collection-
   initializer / property-syntax can wire events without `Set()`.
2. **Property coverage.** The commonly-used WinUI 2 / WinUI 3 properties should be
   first-class on the Reactor element (init-only property or fluent extension), so
   the default scenario *never* requires `.Set(c => c.Foo = bar)`. `.Set()` is the
   escape hatch, not the primary API.
3. **Naming alignment.** Property/factory/element-record names should match the
   WinUI control name they wrap, *or* the deviation should be a deliberate
   stylistic wrapper (e.g. `Heading` over `TextBlock`).

## Methodology

Sources surveyed:

- `src/Reactor/Core/Element.cs` (lines 1643–2948) — all element records.
- `src/Reactor/Elements/Dsl.cs` — factory entry points.
- `src/Reactor/Elements/ElementExtensions.cs` — fluent modifiers.
- `src/Reactor/Controls/**/` — specialized controls (AutoSuggest, DataGrid,
  MaskedTextField, PropertyGrid, VirtualList).

For each control the audit columns are:

| Field | Meaning |
| --- | --- |
| **Factory** | Public DSL entry point in `Factories` |
| **Element** | Backing record in `Microsoft.UI.Reactor.*` |
| **WinUI target** | Native control wrapped (or "(custom)" if Reactor-original) |
| **Events on element** | All `Action`/`Action<T>` callbacks (factory args + init properties) |
| **Events with fluent** | Which of those events also have a `.OnX(handler)` extension |
| **Missing common properties** | WinUI properties used by >50% of real-world WinUI samples that are *not* surfaced as either an init property or a fluent extension |
| **Naming notes** | Deliberate deviation from WinUI name (justified or not) |

---

## §0 Cross-cutting findings

### 0.1 Event-handler fluent extensions are universally missing

The grep below returns zero hits in the entire `src/Reactor` tree:

```
rg -n "public static.*OnClick" src/Reactor
```

**There is no `.OnClick(...)` extension on `ButtonElement`. There is no
`.OnTextChanged(...)` on `TextFieldElement`. There is no `.OnValueChanged(...)`
on `SliderElement` / `NumberBoxElement` / `RatingControlElement`.** Every event
listed in §1–§9 below is only reachable through the factory's positional
parameter list or by the property-initializer syntax (`new ButtonElement("X") {
OnClick = ... }`). For users who prefer the fluent style
`Button("Save").Margin(8).Bold().OnClick(...)`, the chain breaks at the event.

This is a systemic gap, not a per-control miss. The fix is mechanical: emit
one `OnX` extension per element-record callback property. Recommended approach
is a single fluent-extensions file (`ElementExtensions.Events.cs`) or — since
the shapes are extremely regular — a source generator that walks records and
emits one extension per `Action`/`Action<T>` member.

Estimated count of missing extensions: **~60** across all controls (see §1–§9).

### 0.2 `Setters` pattern is consistent — good

Every wrapper element exposes an internal `Setters` array and a public `Set(...)`
extension. The pattern is uniform across all ~70 wrapper records. No change
needed; this is the right escape hatch.

### 0.3 Reactor-original wrappers are clearly named — good

`Heading` / `SubHeading` / `Caption` over `TextBlock`, `VStack` / `HStack` over
`StackPanel`, `Flex` / `FlexRow` / `FlexColumn` (Yoga-based, no WinUI equivalent),
`LazyVStack<T>` / `LazyHStack<T>` (ItemsRepeater wrappers), `TemplatedListView<T>`
etc. — these are deliberate React-/SwiftUI-style affordances and the deviation
is justified. Calling these out in doc comments would help discoverability but
is not a blocker.

### 0.4 A handful of properties shadow WinUI names with different types

`SwipeControlElement.LeftItemsMode` is `SwipeMode` (matches WinUI). But
`StackElement.Spacing` defaults to **8** while WinUI's `StackPanel.Spacing`
defaults to **0** — a deliberate but undocumented opinionated default. Worth
calling out in remediation: keep the default, but document it.

---

## §1 Text & Rich Text

### 1.1 `TextBlock` → `Microsoft.UI.Xaml.Controls.TextBlock`

| Field | Status |
| --- | --- |
| **Factory** | `TextBlock(string content)` |
| **Element** | `TextBlockElement(string Content)` |
| **Events on element** | *(none)* |
| **Events with fluent** | n/a |
| **Init properties** | `FontSize`, `Weight`, `FontStyle`, `HorizontalAlignment`, `TextWrapping`, `TextAlignment`, `TextTrimming`, `IsTextSelectionEnabled`, `FontFamily` |
| **Fluent extensions** | `Bold`, `SemiBold`, `FontSize`, `FontStyle`, `TextWrapping`, `TextAlignment`, `TextTrimming`, `Selectable`, `FontFamily` |
| **Missing common WinUI properties** | `Foreground` (have generic `.Foreground()` on Element — OK), `LineHeight`, `MaxLines`, `CharacterSpacing`, `TextDecorations`, `Inlines` (would require new model) |
| **Naming notes** | Matches WinUI exactly. `Weight` is short for `FontWeight` — minor inconsistency with WinUI's `FontWeight`. |

### 1.2 `Heading` / `SubHeading` / `Caption` (Reactor-original stylized TextBlocks)

| Field | Status |
| --- | --- |
| **Factory** | `Heading(string)`, `SubHeading(string)`, `Caption(string)` |
| **Element** | `TextBlockElement` with preset `FontSize` / `Weight` / heading-level |
| **Naming notes** | Deliberate. Justified: Fluent UI / Material / SwiftUI all have a typography scale; mirroring `TextBlock` styles requires `Style="{StaticResource TitleTextBlockStyle}"`. Recommend: extend the family — `Title`, `Subtitle`, `Body`, `BodyStrong`, `BodyLarge` to align with the WinUI 3 type ramp. |

### 1.3 `RichText` → `Microsoft.UI.Xaml.Controls.RichTextBlock`

| Field | Status |
| --- | --- |
| **Factory** | `RichText(string)`, `RichText(RichTextParagraph[])` |
| **Element** | `RichTextBlockElement(string Text)` + `Paragraphs[]` |
| **Events** | *(none)* |
| **Init properties** | `FontSize`, `Paragraphs`, `IsTextSelectionEnabled`, `TextWrapping` |
| **Missing common properties** | `MaxLines`, `LineHeight`, `OverflowContentTarget` (links to a continuation), `TextAlignment`, `TextTrimming`, `CharacterSpacing` |
| **Naming notes** | Reactor uses `RichText`; WinUI uses `RichTextBlock`. Mild — consider renaming or alias. |

### 1.4 `RichEditBox` → `Microsoft.UI.Xaml.Controls.RichEditBox`

| Field | Status |
| --- | --- |
| **Factory** | `RichEditBox(string text, Action<string>? onTextChanged)` |
| **Element** | `RichEditBoxElement(string Text)` with `OnTextChanged` init |
| **Events on element** | `OnTextChanged` |
| **Events with fluent** | ❌ **Missing** `.OnTextChanged(...)` |
| **Init properties** | `IsReadOnly`, `Header`, `PlaceholderText`, `OnTextChanged` |
| **Missing common properties** | `IsSpellCheckEnabled`, `MaxLength`, `TextWrapping`, `AcceptsReturn`, `SelectionHighlightColor`, `Document` access |
| **Naming notes** | Matches WinUI exactly. |

---

## §2 Buttons

### 2.1 `Button` → `Microsoft.UI.Xaml.Controls.Button`

| Field | Status |
| --- | --- |
| **Factory** | `Button(string label, Action? onClick)`, `Button(Element content, Action? onClick)`, `Button(Command)` |
| **Element** | `ButtonElement(string Label, Action? OnClick)` |
| **Events on element** | `OnClick` |
| **Events with fluent** | ❌ **Missing** `.OnClick(...)` (the canonical example raised by the user) |
| **Init properties** | `IsEnabled`, `IsDisabledFocusable`, `ContentElement` |
| **Fluent extensions** | `DisabledFocusable`, `Disabled` (generic) |
| **Missing common properties** | `Style` (currently only via `.ApplyStyle()` — fine), `Flyout` (have `.WithFlyout()` — OK), `CornerRadius` (generic ✓), `Padding` (generic ✓). Real gap: no `.AccentStyle()` / `.SubtleStyle()` shortcut for the very common `AccentButtonStyle` / `SubtleButtonStyle` resources. |
| **Naming notes** | Matches WinUI exactly. |

### 2.2 `HyperlinkButton` → `Microsoft.UI.Xaml.Controls.HyperlinkButton`

| Field | Status |
| --- | --- |
| **Factory** | `HyperlinkButton(string content, Uri? navigateUri, Action? onClick)`, `HyperlinkButton(Command)` |
| **Element** | `HyperlinkButtonElement(string Content, Uri? NavigateUri, Action? OnClick)` |
| **Events on element** | `OnClick` |
| **Events with fluent** | ❌ **Missing** `.OnClick(...)` |
| **Init properties** | — |
| **Missing common properties** | `NavigateUri` is constructor-only; no fluent `.NavigateUri(...)` despite the XML doc on `Button(Command)` instructing users to "combine with `.NavigateUri(...)` via `.Set()`" — the comment promises an API that doesn't exist. **Fix the doc or add the extension.** |
| **Naming notes** | Matches. |

### 2.3 `RepeatButton` → `Microsoft.UI.Xaml.Controls.Primitives.RepeatButton`

| Field | Status |
| --- | --- |
| **Factory** | `RepeatButton(string label, Action? onClick)`, `RepeatButton(Command)` |
| **Element** | `RepeatButtonElement` with `Delay=250`, `Interval=50` |
| **Events on element** | `OnClick` |
| **Events with fluent** | ❌ Missing |
| **Fluent extensions** | `Delay`, `Interval` ✓ |
| **Missing common properties** | covered |
| **Naming notes** | Matches. |

### 2.4 `ToggleButton` → `Microsoft.UI.Xaml.Controls.Primitives.ToggleButton`

| Field | Status |
| --- | --- |
| **Factory** | `ToggleButton(string label, bool isChecked, Action<bool>? onIsCheckedChanged)`, `ToggleButton(Command, bool isChecked)` |
| **Events on element** | `OnIsCheckedChanged` |
| **Events with fluent** | ❌ Missing `.OnIsCheckedChanged(...)` |
| **Missing common properties** | `IsThreeState` + `IsChecked` as nullable (WinUI exposes `bool?` to support indeterminate). Reactor only supports two states. |
| **Naming notes** | Matches. |

### 2.5 `DropDownButton`, `SplitButton`, `ToggleSplitButton`

| Field | Status |
| --- | --- |
| **Factory** | All match WinUI names. |
| **Events on element** | `SplitButton`: `OnClick`; `ToggleSplitButton`: `OnIsCheckedChanged` |
| **Events with fluent** | ❌ Missing across all three |
| **Missing common properties** | `Flyout` is constructor-only; could be fluent. |
| **Naming notes** | Matches. |

---

## §3 Input controls

### 3.1 `TextField` → `Microsoft.UI.Xaml.Controls.TextBox`

| Field | Status |
| --- | --- |
| **Factory** | `TextField(string value, Action<string>? onChanged, string? placeholder, string? header)` |
| **Element** | `TextFieldElement(string Value, Action<string>? OnChanged, string? Placeholder)` |
| **Events on element** | `OnChanged`, `OnSelectionChanged` |
| **Events with fluent** | ❌ **Both missing** |
| **Init properties** | `Header`, `IsReadOnly`, `AcceptsReturn`, `TextWrapping`, `SelectionStart`, `SelectionLength` |
| **Fluent extensions** | `ReadOnly`, `AcceptsReturn`, `TextWrapping`, `Header` |
| **Missing common properties** | `MaxLength`, `InputScope` (numeric/email/url/etc — *very* common), `IsSpellCheckEnabled`, `CharacterCasing`, `TextAlignment`, `Description` (text under the box) |
| **Naming notes** | **Deliberate deviation.** WinUI is `TextBox`; Reactor uses `TextField` (matches SwiftUI / Fluent UI React / MUI). Justified — `TextField` is the more universally recognized name. Worth documenting prominently. See §16 for a detailed collision analysis if we ever reconsider the rename. |

### 3.2 `PasswordBox` → `Microsoft.UI.Xaml.Controls.PasswordBox`

| Field | Status |
| --- | --- |
| **Factory** | `PasswordBox(string password, Action<string>? onPasswordChanged, string? placeholderText)` |
| **Events on element** | `OnPasswordChanged` |
| **Events with fluent** | ❌ Missing |
| **Init properties** | (just constructor args + `Setters`) |
| **Missing common properties** | `MaxLength`, `Header`, `PasswordRevealMode`, `PasswordChar`, `InputScope` |
| **Naming notes** | Matches. |

### 3.3 `NumberBox` → `Microsoft.UI.Xaml.Controls.NumberBox`

| Field | Status |
| --- | --- |
| **Factory** | `NumberBox(double value, Action<double>? onValueChanged, string? header)` |
| **Events on element** | `OnValueChanged` |
| **Events with fluent** | ❌ Missing |
| **Init properties** | `Minimum`, `Maximum`, `PlaceholderText`, `SpinButtonPlacement`, `SmallChange`, `LargeChange` |
| **Fluent extensions** | `Range`, `SpinButtons` |
| **Missing common properties** | `NumberFormatter` (formatting decimal places — common), `AcceptsExpression`, `ValidationMode`, `Description` |
| **Naming notes** | Matches. `Range(min,max)` is a Reactor convenience helper — keep. |

### 3.4 `AutoSuggestBox` → `Microsoft.UI.Xaml.Controls.AutoSuggestBox`

| Field | Status |
| --- | --- |
| **Factory** | `AutoSuggestBox(string text, Action<string>? onTextChanged, Action<string>? onQuerySubmitted)` |
| **Events on element** | `OnTextChanged`, `OnQuerySubmitted`, `OnSuggestionChosen` (init-only, NOT in factory) |
| **Events with fluent** | ❌ All three missing |
| **Init properties** | `Suggestions`, `PlaceholderText` |
| **Missing common properties** | `Header`, `QueryIcon`, `IsSuggestionListOpen` (manual control), `TextMemberPath` / `DisplayMemberPath` (for non-string suggestions — would need richer model — see also the typed `AutoSuggestElement<T>` in `Controls/AutoSuggest/`) |
| **Naming notes** | Matches. **Bug-worthy**: `OnSuggestionChosen` is reachable only via property-init syntax — neither factory nor fluent. |

### 3.5 `CheckBox` / `ThreeStateCheckBox` → `Microsoft.UI.Xaml.Controls.CheckBox`

| Field | Status |
| --- | --- |
| **Factory** | `CheckBox(bool isChecked, Action<bool>? onIsCheckedChanged, string? label)`, `ThreeStateCheckBox(bool? checkedState, Action<bool?>? onCheckedStateChanged, string? label)` |
| **Events on element** | `OnIsCheckedChanged`, `OnCheckedStateChanged` |
| **Events with fluent** | ❌ Both missing |
| **Naming notes** | Matches. Splitting two/three-state into two factories is a Reactor convenience — fine. |

### 3.6 `RadioButton`, `RadioButtons`

| Field | Status |
| --- | --- |
| **Factory** | `RadioButton(label, isChecked, onIsCheckedChanged, groupName)`, `RadioButtons(string[] items, int selectedIndex, Action<int>? onSelectedIndexChanged)` |
| **Events** | `OnIsCheckedChanged` / `OnSelectedIndexChanged` |
| **Events with fluent** | ❌ Missing |
| **Missing common properties** | `RadioButtons`: `MaxColumns`, `Header` (already there), `ItemTemplate` (would need typed overload) |
| **Naming notes** | Matches. |

### 3.7 `ComboBox` → `Microsoft.UI.Xaml.Controls.ComboBox`

| Field | Status |
| --- | --- |
| **Factory** | `ComboBox(string[] items, int selectedIndex, Action<int>? onSelectedIndexChanged)`, `ComboBox(Element[] itemElements, ...)` |
| **Events on element** | `OnSelectedIndexChanged` |
| **Events with fluent** | ❌ Missing |
| **Init properties** | `PlaceholderText`, `Header`, `IsEditable`, `ItemElements` |
| **Fluent extensions** | `Placeholder`, `Editable`, `Header` |
| **Missing common properties** | `MaxDropDownHeight`, `Description`, `DropDownOpened`/`DropDownClosed` events |
| **Naming notes** | Matches. |

### 3.8 `Slider` → `Microsoft.UI.Xaml.Controls.Slider`

| Field | Status |
| --- | --- |
| **Factory** | `Slider(double value, double min, double max, Action<double>? onValueChanged)` |
| **Events on element** | `OnValueChanged` |
| **Events with fluent** | ❌ Missing |
| **Init properties** | `StepFrequency`, `Header` |
| **Fluent extensions** | `StepFrequency`, `Header` |
| **Missing common properties** | `Orientation`, `TickFrequency`, `TickPlacement`, `SnapsTo`, `IsThumbToolTipEnabled`, `ThumbToolTipValueConverter` |
| **Naming notes** | Matches. |

### 3.9 `ToggleSwitch` → `Microsoft.UI.Xaml.Controls.ToggleSwitch`

| Field | Status |
| --- | --- |
| **Factory** | `ToggleSwitch(bool isOn, Action<bool>? onIsOnChanged, string? onContent, string? offContent, string? header)` |
| **Events on element** | `OnIsOnChanged` |
| **Events with fluent** | ❌ Missing |
| **Init properties** | `Header` |
| **Fluent extensions** | `Header` |
| **Missing common properties** | `OffContent`/`OnContent` as `Element` (not just `string`), `IsEnabled` (generic ✓) |
| **Naming notes** | Matches. |

### 3.10 `RatingControl` → `Microsoft.UI.Xaml.Controls.RatingControl`

| Field | Status |
| --- | --- |
| **Events** | `OnValueChanged` |
| **Events with fluent** | ❌ Missing |
| **Missing common properties** | `Caption` is init but no fluent. `PlaceholderValue`, `InitialSetValue` |
| **Naming notes** | Matches. |

### 3.11 `ColorPicker` → `Microsoft.UI.Xaml.Controls.ColorPicker`

| Field | Status |
| --- | --- |
| **Events** | `OnColorChanged` |
| **Events with fluent** | ❌ Missing |
| **Init properties** | `IsAlphaEnabled`, `IsMoreButtonVisible`, `IsColorSpectrumVisible`, `IsColorSliderVisible`, `IsColorChannelTextInputVisible`, `IsHexInputVisible` |
| **Fluent extensions** | none (only `.Set()`) |
| **Missing common properties** | All six init properties have no fluent equivalent. Also missing: `ColorSpectrumShape`, `MinHue`/`MaxHue`/`MinSaturation`/`MaxSaturation`/`MinValue`/`MaxValue` |
| **Naming notes** | Matches. |

---

## §4 Date & Time

### 4.1 `CalendarDatePicker`, `DatePicker`, `TimePicker`, `CalendarView`

| Control | Events | Fluent? | Notable gaps |
| --- | --- | --- | --- |
| `CalendarDatePicker` | `OnDateChanged` | ❌ | `DateFormat`, `IsTodayHighlighted`, `IsCalendarOpen`, `IsGroupLabelVisible` |
| `DatePicker` | `OnDateChanged` | ❌ | `DayFormat`, `MonthFormat`, `YearFormat`, `Orientation`, `DayPicker`/`MonthPicker`/`YearPicker` order |
| `TimePicker` | `OnTimeChanged` | ❌ | `Header` (init only, no fluent), no event fluent |
| `CalendarView` | none modeled | n/a | `SelectedDatesChanged` (the *only* meaningful event — **missing entirely**), `MinDate`/`MaxDate`, `FirstDayOfWeek`, `NumberOfWeeksInView`, `DisplayMode` |

**`CalendarView` is the worst offender in this section** — it wraps a control whose
entire purpose is letting users select dates, but Reactor exposes no
`SelectedDates` collection and no `OnSelectedDatesChanged` event. Currently
unreachable except via `.Set()`.

---

## §5 Progress & Status

| Control | Events | Fluent? | Notable gaps |
| --- | --- | --- | --- |
| `Progress` (ProgressBar) | none | n/a | matches WinUI sufficiently |
| `ProgressRing` | none | n/a | covered |
| `InfoBar` | `OnActionButtonClick`, `OnClosed` | ❌ both | `IconSource` (custom icon), `Content` (element child) |
| `InfoBadge` | none | n/a | `IconSource`, `Background` (have generic ✓) |

**Naming note:** `Progress` (Reactor) vs `ProgressBar` (WinUI). Minor deviation —
the factory name dropped "Bar" for brevity. The `ProgressIndeterminate()`
helper is a Reactor-original convenience. Worth documenting.

---

## §6 Layout containers

| Control | WinUI target | Events | Events with fluent | Notable gaps |
| --- | --- | --- | --- | --- |
| `VStack`/`HStack` (`StackElement`) | `StackPanel` | none | n/a | matches |
| `WrapGrid` (`WrapGridElement`) | `VariableSizedWrapGrid` | none | n/a | `ColumnSpan`/`RowSpan` attached props |
| `Grid` (`GridElement`) | `Grid` | none | n/a | covered |
| `ScrollView` | `ScrollViewer` | none on element | — | **Missing** `ViewChanged` event entirely. Generic `OnSizeChanged` exists but scroll-position changes are unreachable. |
| `Border` | `Border` | none | n/a | covered |
| `Expander` | `Expander` | `OnIsExpandedChanged` | ❌ | `ExpandDirection` ✓, `HeaderTemplate`, `ContentTransitions` |
| `SplitView` | `SplitView` | `OnPaneOpenChanged` | ❌ | `PaneBackground`, `LightDismissOverlayMode` |
| `Viewbox` | `Viewbox` | none | n/a | `StretchDirection` |
| `Canvas` | `Canvas` | none | n/a | covered |
| `Flex`/`FlexRow`/`FlexColumn` | (custom, Yoga) | none | n/a | Reactor-original; covered |
| `RelativePanel` | `RelativePanel` | none | n/a | attached-property-only, fine via `.RelativeTo()` helpers |

**Naming notes:**

- `VStack`/`HStack` over `StackPanel` — deliberate (SwiftUI-style), justified.
- `ScrollView` over `ScrollViewer` — minor; consider an alias or rename.

---

## §7 Navigation

### 7.1 `NavigationHost<TRoute>` (Reactor-original)

| Field | Status |
| --- | --- |
| **Element** | `NavigationHostElement` |
| **Events** | none (handled by `Navigation.NavigationHandle`) |
| **Init properties** | `Transition`, `CacheMode`, `CacheSize` |
| **Naming notes** | Reactor-original — no WinUI equivalent. |

### 7.2 `NavigationView` → `Microsoft.UI.Xaml.Controls.NavigationView`

| Field | Status |
| --- | --- |
| **Events on element** | `OnSelectedTagChanged`, `OnBackRequested` |
| **Events with fluent** | ❌ both missing (note: there *is* a Navigation-handle binding helper that auto-wires both, but the manual handler path has no fluent) |
| **Fluent extensions** | `PaneDisplayMode`, `PaneTitle` |
| **Missing common properties** | `AutoSuggestBox` slot, `PaneFooter`, `PaneCustomContent`, `MenuItemsSource` (already partly via `MenuItems[]`), `OpenPaneLength`, `CompactModeThresholdWidth`, `ExpandedModeThresholdWidth` |

### 7.3 `TitleBar` → `Microsoft.UI.Xaml.Controls.TitleBar` (WinUI 3 1.5+)

| Field | Status |
| --- | --- |
| **Events** | `OnBackRequested`, `OnPaneToggleRequested` |
| **Events with fluent** | ❌ both missing |
| **Init properties** | `Subtitle`, `IsBackButtonVisible`, `IsBackButtonEnabled`, `IsPaneToggleButtonVisible`, `Content`, `RightHeader`, `Icon` |
| **Fluent extensions** | `Subtitle` only |
| **Missing common properties** | most init properties have no fluent — `IsBackButtonVisible` / `IconSource` / `RightHeader` are the most-used |

### 7.4 `TabView` → `Microsoft.UI.Xaml.Controls.TabView`

| Field | Status |
| --- | --- |
| **Events** | `OnSelectedIndexChanged`, `OnTabCloseRequested`, `OnAddTabButtonClick` |
| **Events with fluent** | ❌ all three missing |
| **Fluent extensions** | `ShowAddButton` only |
| **Missing common properties** | `TabWidthMode`, `CloseButtonOverlayMode`, `CanDragTabs`, `CanReorderTabs`, `AllowDropTabs`, `TabStripHeader`/`TabStripFooter` |

### 7.5 `BreadcrumbBar`, `Pivot`, `Frame`

| Control | Events | Fluent? | Notable gaps |
| --- | --- | --- | --- |
| `BreadcrumbBar` | `OnItemClicked` (factory arg) | ❌ | `ItemTemplate` |
| `Pivot` | `OnSelectedIndexChanged` | ❌ | `LeftHeader`/`RightHeader`, `HeaderTemplate`, `IsHeaderItemsCarouselEnabled` |
| `Frame` | none modeled | n/a | `Navigated`/`Navigating`/`NavigationFailed` events all missing; users must `.Set()` |

---

## §8 Collection controls

| Control | Events | Fluent? | Notable gaps |
| --- | --- | --- | --- |
| `ListView` | `OnSelectedIndexChanged`, `OnItemClick` | ❌ both | `IsItemClickEnabled` (implicit), `ItemContainerStyle`, `GroupStyle`, `IncrementalLoadingTrigger` |
| `GridView` | `OnSelectedIndexChanged`, `OnItemClick` | ❌ both | same as ListView |
| `TreeView` | `OnItemInvoked`, `OnExpanding` | ❌ both | `Collapsed` event, `ItemTemplate`, drag/drop properties exist but no event fluents |
| `FlipView` | `OnSelectedIndexChanged` | ❌ | `UseTouchAnimationsForAllNavigation`, `MaxItems` |
| `ListBox` | `OnSelectedIndexChanged` | ❌ | `SelectionMode`, multi-select indexes, header/footer |
| `ItemsView<T>` | `OnItemInvoked` | ❌ | `IsItemInvokedEnabled` ✓; missing `ScrollView` slot access, `Layout` (have `LayoutKind` enum — fine) |
| `SemanticZoom` | none modeled | n/a | `ViewChangeStarted`/`Completed` missing |
| `TemplatedListView<T>`/`TemplatedGridView<T>`/`TemplatedFlipView<T>` | `OnSelectedIndexChanged`, `OnItemClick` | ❌ | same as non-templated |
| `LazyVStack<T>`/`LazyHStack<T>` | none | n/a | Reactor-original wrapper around `ItemsRepeater` |

**Universal gap:** No collection control exposes `SelectionChanged` as a multi-
selection event (only by-index). For `SelectionMode != Single`, users have no
typed entry point. Track as a follow-up.

---

## §9 Dialogs, overlays, flyouts, menus

| Control | Events | Fluent? | Notable gaps |
| --- | --- | --- | --- |
| `ContentDialog` | `OnClosed(ContentDialogResult)` | ❌ | `IsPrimaryButtonEnabled`/`IsSecondaryButtonEnabled`, `PrimaryButtonCommand` patterns, `Opened`/`Opening` events |
| `Flyout` | `OnOpened`, `OnClosed` | ❌ both | `ShowMode`, `AreOpenCloseAnimationsEnabled`, `OverlayInputPassThroughElement` |
| `TeachingTip` | `OnActionButtonClick`, `OnClosed` | ❌ both | `IconSource`, `HeroContent`, `PlacementMargin`, `PreferredPlacement` |
| `Popup` | `OnClosed` | ❌ | `Opened` event missing entirely |
| `InfoBar` | (see §5) | | |
| `MenuBar`/`CommandBar`/`MenuFlyout`/`CommandBarFlyout` | none modeled at element level (events live on `MenuFlyoutItemData` records) | n/a | `Opening`/`Closing` for flyouts unmodeled. `CommandBar.IsOpenChanged` unmodeled. |

---

## §10 Media

| Control | Events | Fluent? | Notable gaps |
| --- | --- | --- | --- |
| `Image` | none | n/a | `ImageOpened`/`ImageFailed`, `NineGrid` |
| `PersonPicture` | none | n/a | covered (DisplayName/Initials/ProfilePicture/IsGroup/BadgeNumber all available) |
| `WebView2` | `OnNavigationStarting(Uri)`, `OnNavigationCompleted(Uri)` | ❌ both | `WebMessageReceived`, `CoreWebView2Initialized`, `Source` is init-only |
| `MediaPlayerElement` | none modeled | n/a | `MediaPlayer.MediaOpened`/`MediaEnded`/`MediaFailed` unreachable |
| `AnimatedVisualPlayer` | none modeled | n/a | `Loaded`, no playback control surface |
| `AnimatedIcon` | none | n/a | `State` (init-only enum string property would help) |

---

## §11 Shapes

| Control | Events | Notable gaps |
| --- | --- | --- |
| `Rectangle` | none | covered |
| `Ellipse` | none | covered |
| `Line` | none | covered (X1/Y1/X2/Y2 ✓) |
| `Path` | none | `StrokeStartLineCap`, `StrokeEndLineCap`, `StrokeLineJoin`, `StrokeMiterLimit`, `StrokeDashCap`, `StrokeDashOffset`, `FillRule` |

Naming and structure match WinUI Shapes. Path has the most gaps because it has
the most native properties.

---

## §12 Niche / less-common

| Control | Events | Notes |
| --- | --- | --- |
| `SelectorBar` | `OnSelectedIndexChanged` | ❌ no fluent |
| `PipsPager` | `OnSelectedPageIndexChanged` | ❌ no fluent. Missing `WrapMode`, `MaxVisiblePips`, `PreviousButtonVisibility`, `NextButtonVisibility` |
| `AnnotatedScrollBar` | none modeled | the entire `DetailLabelRequested` + `ScrollingAnimationStarting` surface is absent |
| `RefreshContainer` | `OnRefreshRequested` | ❌ no fluent. `PullDirection` missing |
| `SwipeControl` | none on element (events on `SwipeItemData.OnInvoked`) | covered |
| `ParallaxView` | none | `VerticalShift`/`HorizontalShift` ✓, missing `Source` (binding target), `VerticalSourceStartOffset`/`EndOffset` |
| `MapControl` | none modeled | The entire Maps API surface (`Center`, `MapElements`, `ViewChanged`) is unreachable |

---

## §13 Specialized Reactor controls

These are Reactor-original (no WinUI equivalent) so naming is "match itself."

| Control | File | Notes |
| --- | --- | --- |
| `AutoSuggestElement<T>` | `Controls/AutoSuggest/` | Typed peer of `AutoSuggestBox` — different from `AutoSuggestBoxElement`. Audit separately. |
| `DataGridElement<T>` | `Controls/DataGrid/` | First-party data grid. Audit separately. |
| `MaskedTextFieldElement` | `Controls/MaskedTextBox/` | First-party. Folder is `MaskedTextBox/` but the type/factory are `MaskedTextField`. If/when we resolve the `TextField`-vs-`TextBox` deviation (§3.1 / §16), the natural follow-on is `MaskedTextField` → `MaskedTextBox` so folder/type/factory all agree. WinForms also names this control `MaskedTextBox`. Audit otherwise separately. |
| `PropertyGridElement` | `Controls/PropertyGrid/` | First-party. Audit separately. |
| `VirtualListElement` | `Controls/Virtualization/` | Generic virtualization. Audit separately. |

Recommend a follow-up spec **040-specialized-control-scrub** that runs the same
three checks against these five.

---

## §14 Summary — recommended remediation order

Ordered roughly by user-visible impact:

1. **Generate `On…` fluent extensions for every event-callback element property.**
   Single change touching one file (or one source generator). Closes the
   universal §0.1 gap, ~60 extension methods. *No semantic change*; pure
   ergonomic win.
2. **Promote frequently-set init properties to fluent extensions for the highest-
   traffic controls** (`Slider`, `NumberBox`, `ColorPicker`, `TabView`,
   `NavigationView`, `TitleBar`, `TextField`). Most of these have rich init
   surfaces but only one or two fluent helpers.
3. **Model the missing events on `CalendarView`, `Frame`, `ScrollView`, `Popup`,
   `MediaPlayerElement`, `WebView2`** — these have meaningful events that are
   currently unreachable without `.Set()`. Spec-level decision on each.
4. **Common-property gaps with high traffic**: `TextField.InputScope`,
   `TextField.MaxLength`, `Slider.TickFrequency`/`Orientation`,
   `NumberBox.NumberFormatter`, `ContentDialog.IsPrimaryButtonEnabled`,
   `RichTextBlock.MaxLines`, `TabView.TabWidthMode`/`CanReorderTabs`.
5. **Fix the `HyperlinkButton(Command)` doc comment** that promises a
   `.NavigateUri(...)` extension that does not exist.
6. **Add type-ramp factories** alongside `Heading`/`SubHeading`/`Caption`:
   `Title`, `Subtitle`, `Body`, `BodyStrong`, `BodyLarge`, matching the WinUI 3
   type ramp.
7. **Specialized controls audit** — separate spec 040, covering AutoSuggest,
   DataGrid, MaskedTextField, PropertyGrid, VirtualList.
8. Fix the RichText to be RichTextBlock, consistent with TextBlock and the WinUI RichTextBlock control.


## §15 Open questions

1. Should event extensions be hand-written or source-generated? Pro source-gen:
   keeps shapes consistent and stays in sync as records evolve. Con: another
   moving piece.
2. For nullable callbacks (`Action<bool>?`), should `.OnX(null)` clear a
   previously-set handler, or should we offer a separate `.WithoutOnX()`? The
   record-init pattern already permits clearing; a fluent `null`-passing
   convention is fine.
   A: null -> clear, is good

3. Should we add `.AccentButton()` / `.SubtleButton()` as fluent shortcuts for
   the very common WinUI button styles, or insist users go through `.ApplyStyle`?
   A: .AccentButton()/.SubtleButton()... or "AccentButton()", etc., similar to the Heading/etc. for TextBlock.

4. `CalendarView.OnSelectedDatesChanged` — multi-selection requires returning
   a `DateTimeOffset[]`. The reactor pattern so far has been "single-index ints",
   so multi-select calendars need a deliberate shape decision.
   A: I'm OK with having distinct API for CalendarView

5. Do we want a fluent `.NavigateUri(...)` extension on `HyperlinkButton`, or
   fix the doc comment to point at the constructor arg?
   A: add the fluent API .

---

## §16 Investigation — `TextField` → `TextBox` rename (and `MaskedTextField` → `MaskedTextBox`)

The §3.1 naming deviation surfaces a recurring question: should Reactor align
with the WinUI name (`TextBox`) instead of the React-/SwiftUI-flavored
`TextField`? This section captures the mechanical impact in case the policy
choice changes later. **No rename is recommended at this time** — the
deviation argument in §3.1 stands. This is reference material.

### 16.1 In-tree impact

- **~578 source occurrences** of `TextField` across ~100+ files: samples,
  tests, docs/guides, skills/recipes, plugins, pipeline templates.
- **Hardcoded factory-name strings** that drive analyzers / tooling and would
  need updating in lockstep:
  - `src/Reactor.Analyzers/AccessibilityAnalyzers.cs:198` — input-control
    factory-name hashset.
  - `src/Reactor.Cli/Loc/LocalizableStringScanner.cs:27, :136` — localization
    scanner keys on the method name.
- **165 files** combine `using static Microsoft.UI.Reactor.Factories;` with
  `using Microsoft.UI.Xaml.Controls;` — the only scopes where collision is
  possible.

### 16.2 Compile-time collisions

`TextBoxElement` (renamed record) does **not** collide with WinUI's `TextBox`
type — different bare names. The factory method `Factories.TextBox(...)` would
share the bare name `TextBox` with the WinUI type in those 165 files. C#
context-resolves most cases:

| Usage | Resolution | Risk |
| --- | --- | --- |
| `TextBox("value", onChanged)` | Reactor factory method | ✅ unambiguous |
| `new TextBox()` | WinUI type | ✅ unambiguous |
| `(TextBox)x`, `typeof(TextBox)`, `Foo<TextBox>` | WinUI type | ✅ unambiguous |
| `.Set(tb => …)` inside a Reactor element | `tb` is type-inferred | ✅ no naming needed |
| `nameof(TextBox)` | **Ambiguous** between type and method group | ⚠️ user footgun (0 hits today) |
| `Action<string> a = TextBox;` | Method-group conversion to factory | ⚠️ resolves but surprises |

Inside Reactor itself the library does not use `using static Factories;`, so
the library code (Reconciler, ElementPool, etc.) is unaffected.

### 16.3 The `MaskedTextField` companion rename

If `TextField` is ever renamed to `TextBox`, `MaskedTextField` should rename
to `MaskedTextBox` in the same change:

- The folder is already `src/Reactor/Controls/MaskedTextBox/` — the type
  name is the only thing inconsistent today.
- The Windows-platform name for this control is `MaskedTextBox` (WinForms).
- Bundling the two renames avoids a second migration pass.
- No collision: `MaskedTextBoxElement` ≠ any WinUI / WinForms type name, and
  no WinUI control method named `MaskedTextBox` exists.

### 16.4 Migration options (if/when we do it)

- **A. Hard cutover.** Compile-clean rename across the 578 occurrences. Live
  with the `nameof` footgun (document it).
- **B. `[Obsolete]` forwarding alias for one release.** `TextField` stays as
  a thin wrapper calling `TextBox` (and same for `TextFieldElement` →
  `TextBoxElement`, `MaskedTextField` → `MaskedTextBox`). Lets external users
  migrate without breaking; we strip the aliases the release after.
- **C. Don't rename.** The deliberate-deviation argument in §3.1 stands;
  action shrinks to one XML-doc-comment line on the `TextField` factory
  explaining the naming choice. **Currently recommended.**

### 16.5 Decision

**Keep `TextField` and `MaskedTextField` as-is for now (option C).** Record
the deviation in the doc comments. If we revisit later, bundle both renames
together and pick (B) for the migration path. This subsection is the
reference for the impact analysis.

---

## §17 Named-style fluent helpers

Sitting alongside `.AccentButton()` / `.SubtleButton()` (§15 Q3), these
promote frequently-used WinUI named styles or enum-property values to fluent
helpers, so the common case never requires `.ApplyStyle("…")` or a verbose
init-property assignment. Each one is a single-line extension; the savings
are discoverability and IntelliSense surface area, not lines of code per
call site.

Decision rule (mirrors §15 Q3): when both a factory and a fluent could
plausibly exist, pick whichever matches the precedent — fluent for style
decorations that compose with any element of a family, factory for shape-
changing wrappers like `Heading`/`Card`.

### 17.1 `.AccentButton()` / `.SubtleButton()` — `Button` and subtypes

- Promotes `AccentButtonStyle` / `SubtleButtonStyle` resources.
- **Fluent**, not factory — works on any `ButtonBase`-derived element
  (`Button`, `DropDownButton`, `SplitButton`). Style choice is decoration,
  not a different element shape.

### 17.2 `.TextLink()` — `HyperlinkButton` (and `Button`)

- Promotes `TextBlockButtonStyle` — chromeless inline-link style. The
  canonical "Learn more" rendering inside paragraphs.
- **Fluent.** Most common on `HyperlinkButton`; consider an overload for
  `Button` too since `TextBlockButtonStyle` is defined for the base
  `Button` class in WinUI.
- Naming: `.TextLink()` reads better than `.AsTextLink()` or
  `.TextBlockStyle()`.

### 17.3 `.NumericInput()` / `.EmailInput()` / `.UrlInput()` / `.PhoneInput()` / `.SearchInput()` — `TextField` (and `PasswordBox` where applicable)

- Promotes `InputScope` / `InputScopeNameValue` enum values. Not a "style"
  in the WinUI sense, but the same ergonomic problem — verbose enum names
  (`InputScopeNameValue.EmailSmtpAddress`) that nobody types by hand.
- **Fluent.** Mirrors React Native's `keyboardType="numeric"` and
  HTML's `<input type="email">`.
- Mapping:
  - `.NumericInput()` → `InputScopeNameValue.Number`
  - `.EmailInput()`  → `InputScopeNameValue.EmailSmtpAddress`
  - `.UrlInput()`    → `InputScopeNameValue.Url`
  - `.PhoneInput()`  → `InputScopeNameValue.TelephoneNumber`
  - `.SearchInput()` → `InputScopeNameValue.Search`
- Generic `.InputScope(InputScopeNameValue)` escape hatch surfaces the long
  tail (`Chat`, `FormulaNumber`, `AlphanumericFullWidth`, etc.) — same
  relationship `.ApplyStyle()` has to `.AccentButton()`. **Decided: include
  the escape hatch.**

### 17.4 `.Informational()` / `.Success()` / `.Warning()` / `.Error()` — `InfoBar`

- Promotes `InfoBarSeverity` enum values. The InfoBar visually re-skins per
  severity (icon + accent color), so this is the closest analogue to a
  named style.
- **Fluent.** Mirrors React/Fluent UI's `<Alert severity="error">`.
- All four severities map 1:1 to `InfoBarSeverity` members; same naming
  as the enum minus the type prefix.

### 17.5 `Card(Element child)` factory — Reactor-original

- **No WinUI named style equivalent** — there is no `CardStyle`. But the
  canonical card composition (corner radius 8, `CardBackgroundFillColorDefaultBrush`,
  ~16 padding, subtle 1px stroke) is written by hand in nearly every WinUI
  app today.
- **Factory returning `BorderElement`** — matches the `Heading` precedent
  exactly: `Heading(string)` returns a preset `TextBlockElement`; `Card(child)`
  returns a preset `BorderElement`. Lets you write `Card(VStack(…))` directly
  and still chain any `Border` fluent extension (`.Padding`, `.CornerRadius`,
  `.Background`, `.WithBorder`) to override the preset.
- Defaults pull from theme resources (`CardBackgroundFillColorDefaultBrush`,
  `CardStrokeColorDefaultBrush`, theme-aware corner radius), not literals,
  so the card re-themes correctly (light/dark, contrast modes).

### 17.6 Items deliberately NOT promoted

- **TextBlock type ramp** (`Title`/`Subtitle`/`Body`/`BodyStrong`/`BodyLarge`)
  — already factories per §14 #6. Don't also add fluents (two-ways trap).
- **Progress `.ShowError()` / `.Paused()`** — too narrow; the existing init
  properties are already cheap to set, and the names don't add
  discoverability the way severity does.
- **NavigationView/TitleBar/TabView pane-display modes etc.** — already
  enum-typed init properties with reasonable names; no ergonomic gap.

### 17.7 Implementation note

All items above — 2 fluents in 17.1, 1 (or 2) in 17.2, 5 named-input fluents
plus 1 generic escape-hatch in 17.3, 4 fluents in 17.4, and 1 factory in
17.5 — fit into the same `ElementExtensions.NamedStyles.cs` file (and
`Factories.NamedStyles.cs` for the `Card` factory, sitting next to where
`Heading`/`SubHeading`/`Caption` live today). Trivial to land in one PR
alongside §14 #1 (the `On…` fluent generator).

The `Card` factory is the only item that needs a theme-resource lookup —
the others are pure property assignments. Lift the brush/stroke lookup
into a small helper so light/dark/contrast switches re-resolve through the
existing `ThemeResource` plumbing rather than baking literals.
