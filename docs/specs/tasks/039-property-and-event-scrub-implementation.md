# Property & Event API Scrub — Implementation Tasks

Derived from: `docs/specs/039-property-and-event-scrub.md`

Scope reminder: spec 039 is an audit. This task list converts every gap it
identified into ship-ready work — new fluent extensions, missing events,
missing properties, naming fixes, doc-comment corrections, named-style
helpers, samples, guides, agent-kit skills, and tests. Tasks are sized to be
paused/resumed; complete top-to-bottom within a phase. Cross-phase ordering
matters (don't ship docs for fluents that aren't generated yet; don't update
samples before the underlying API exists).

Conventions:
- All element records live under `src/Reactor/Core/Element.cs`; factories in
  `src/Reactor/Elements/Dsl.cs`; fluent modifiers in
  `src/Reactor/Elements/ElementExtensions.cs`. New event fluents go in
  `src/Reactor/Elements/ElementExtensions.Events.cs`; named-style fluents in
  `src/Reactor/Elements/ElementExtensions.NamedStyles.cs`; new factories
  (e.g. `Card`, type-ramp `Title`/`Subtitle`/...) in
  `src/Reactor/Elements/Factories.NamedStyles.cs`.
- Spec section anchors are referenced in task bodies (e.g. `(spec §3.1)`).
- Public API additions need XML doc comments (no `CS1591`).
- Code must compile under `Reactor.slnx` warnings-as-errors.
- New unit tests live under `tests/Reactor.Tests/`. Public-API surface tests
  live under `tests/Reactor.SelfTests/` if that's the established pattern for
  surface scrubs (verify with a grep before adding the first one).
- Samples for newly-exposed controls go under `samples/ReactorGallery/ControlPages/`.
- Agent-kit references live under `plugins/reactor/skills/reactor-dsl/references/`
  and `plugins/reactor/skills/reactor-recipes/references/`; the human guide
  under `docs/guide/`.

A task is "done" only when:
1. Code compiles under `Reactor.slnx` warnings-as-errors.
2. Public API surface has XML doc comments (no `CS1591`).
3. New unit tests cover the happy path **and** every documented failure mode.
4. Accessibility analyzers (`REACTOR_A11Y_001..003`) remain clean.
5. Doc + sample + agent-kit skill updates land in the same PR as the API
   change so the surface is discoverable the moment it ships.

---

## Phase 0: Decisions captured & scaffolding

### 0.1 Lock in the open-question decisions from spec §15

- [x] Confirm in the spec header that question answers are final and won't
      change scope mid-implementation: (Q1) source-generated fluents preferred
      over hand-written; (Q2) `.OnX(null)` clears handler; (Q3) add fluent
      `.AccentButton()`/`.SubtleButton()`; (Q4) `CalendarView` gets a distinct
      multi-select shape; (Q5) add fluent `.NavigateUri(...)` on
      `HyperlinkButton`.
- [x] If Q1 lands as "source-gen", add a one-paragraph note to the spec
      pointing at the generator file path so future readers find it; if Q1
      flips to "hand-written", strike the source-gen scaffolding task (0.4).
      **Resolution: hand-written. 0.4 struck.** Spec header also documents
      the C# property-vs-extension delegate-invocation clash discovered
      during implementation — fluents drop the leading `On` (e.g.
      `.Click(handler)` sets property `OnClick`).

### 0.2 New files — empty placeholders compile first, populated later

- [x] Create empty `src/Reactor/Elements/ElementExtensions.Events.cs`
      (namespace + empty `public static partial class ElementExtensions`).
- [x] Create empty `src/Reactor/Elements/ElementExtensions.NamedStyles.cs`
      (same shape).
- [x] Create empty `src/Reactor/Elements/Factories.NamedStyles.cs`
      (namespace + `public static partial class Factories`).
- [x] Verify the solution still builds with these empty partial files.

### 0.3 Inventory the ~60 event-callback properties

- [x] Generate a CSV under `tests/Reactor.SelfTests/Fixtures/event-fluent-inventory.csv`
      with columns `Element, PropertyName, DelegateType, Source(spec §)`.
      Drive it from a one-off script (commit the script too under
      `tools/api-scrub/`) so the inventory regenerates as records evolve.
      Script: `tools/api-scrub/Build-EventFluentInventory.ps1`.
- [x] Cross-check the CSV count against the spec's "~60" estimate; reconcile
      any discrepancy by spot-checking spec §1–§9 and updating the spec if
      the count drifted. **68 rows; §1–§9 ≈ 60, remainder is §12 niche +
      §13 specialized — matches the "~60 across §1–§9" estimate.**

### 0.4 Source generator scaffolding (only if Q1 = source-gen)

- [x] ~~Add a new project `src/Reactor.Generators/Reactor.Generators.csproj`~~
      **Struck per Q1 resolution above (hand-written chosen).**
- [x] ~~Implement the generator…~~ **Struck.**
- [x] ~~Generator must respect Q2…~~ **Struck.**
- [x] ~~Snapshot-test the generator output…~~ **Struck.**

---

## Phase 1: §0.1 — Universal `OnX` fluent extensions

This phase closes the spec's single biggest gap. Every callback property in
the inventory from 0.3 gets a fluent extension. If 0.4 generator landed,
this phase is mechanical verification; otherwise each item below is a
hand-written one-line extension.

**Naming note:** all extensions below drop the leading `On` to avoid the
C# property-vs-extension clash (see Phase 0.1). Property `OnClick` is
reached via `.Click(handler)`, `OnTextChanged` via `.TextChanged(handler)`,
etc. Property names are unchanged.

### 1.1 Buttons (§2)

- [x] `Button.Click(Action? handler)` (spec §2.1 — canonical example).
- [x] `HyperlinkButton.Click(Action? handler)` (spec §2.2).
- [x] `RepeatButton.Click(Action? handler)` (spec §2.3).
- [x] `ToggleButton.IsCheckedChanged(Action<bool>? handler)` (spec §2.4).
- [x] `SplitButton.Click`, `ToggleSplitButton.IsCheckedChanged` (spec §2.5).
      (`DropDownButton` has no click handler — fires its flyout instead.)

### 1.2 Input controls (§3)

- [x] `TextField.Changed(Action<string>? handler)`,
      `TextField.SelectionChanged(...)` (spec §3.1).
- [x] `PasswordBox.PasswordChanged(...)` (spec §3.2).
- [x] `NumberBox.ValueChanged(Action<double>? handler)` (spec §3.3).
- [x] `AutoSuggestBox.TextChanged`, `.QuerySubmitted`,
      `.SuggestionChosen` (spec §3.4 — `SuggestionChosen` was previously
      unreachable from factory; flag fixed in PR description).
- [x] `CheckBox.IsCheckedChanged(Action<bool>? handler)` and
      `.CheckedStateChanged(Action<bool?>? handler)` (spec §3.5).
- [x] `RadioButton.IsCheckedChanged`,
      `RadioButtons.SelectedIndexChanged` (spec §3.6).
- [x] `ComboBox.SelectedIndexChanged(Action<int>? handler)` (spec §3.7).
- [x] `Slider.ValueChanged(Action<double>? handler)` (spec §3.8).
- [x] `ToggleSwitch.IsOnChanged(Action<bool>? handler)` (spec §3.9).
- [x] `RatingControl.ValueChanged(Action<double>? handler)` (spec §3.10).
- [x] `ColorPicker.ColorChanged(Action<Windows.UI.Color>? handler)`
      (spec §3.11).
- [x] `RichEditBox.TextChanged(...)` (spec §1.4).

### 1.3 Date & Time (§4)

- [x] `CalendarDatePicker.DateChanged`, `DatePicker.DateChanged`,
      `TimePicker.TimeChanged` (spec §4).
- [x] `CalendarView.SelectedDatesChanged(...)` — modelled in Phase 3.1;
      fluent landed alongside the event.

### 1.4 Progress, navigation, layout (§5–§7)

- [x] `InfoBar.ActionButtonClick`, `.Closed` (spec §5).
- [x] `Expander.IsExpandedChanged`, `SplitView.PaneOpenChanged`
      (spec §6).
- [x] `NavigationView.SelectedTagChanged`, `.BackRequested` (spec §7.2).
- [x] `TitleBar.BackRequested`, `.PaneToggleRequested` (spec §7.3).
- [x] `TabView.SelectedIndexChanged`, `.TabCloseRequested`,
      `.AddTabButtonClick` (spec §7.4).
- [x] `BreadcrumbBar.ItemClicked`, `Pivot.SelectedIndexChanged` (spec §7.5).

### 1.5 Collections, dialogs, media, niche (§8–§12)

- [x] `ListView`, `GridView`, `TreeView`, `FlipView`, `ListBox`,
      `ItemsView<T>`, `TemplatedListView<T>`, `TemplatedGridView<T>`,
      `TemplatedFlipView<T>` — `.SelectedIndexChanged`/`.ItemClick`/
      `.ItemInvoked`/`.Expanding` per spec §8.
- [x] `ContentDialog.Closed(Action<ContentDialogResult>? handler)`
      (spec §9).
- [x] `Flyout.Opened`, `.Closed`; `TeachingTip.ActionButtonClick`,
      `.Closed`; `Popup.Closed` (spec §9).
- [x] `WebView2.NavigationStarting(Action<Uri>?)`,
      `.NavigationCompleted(Action<Uri>?)` (spec §10).
- [x] `SelectorBar.SelectedIndexChanged`,
      `PipsPager.SelectedPageIndexChanged`,
      `RefreshContainer.RefreshRequested` (spec §12).

### 1.6 Null-clear semantics (Q2 from §15)

- [x] Add one unit test per fluent that confirms passing `null` clears any
      previously-set handler and that re-applying restores it. Test file
      `tests/Reactor.Tests/Elements/EventFluentNullClearTests.cs` —
      63 facts, one per fluent.

---

## Phase 2: §17 — Named-style fluent helpers and `Card` factory

### 2.1 Button style fluents (§17.1)

- [x] `.AccentButton()` on `ButtonElement` and any subtype declared in the
      record hierarchy (`DropDownButtonElement`, `SplitButtonElement`,
      `ToggleSplitButtonElement`). Uses `AccentButtonStyle` static resource.
- [x] `.SubtleButton()` likewise (`SubtleButtonStyle`).
- [x] Both helpers work via the WinUI `ThemeResource` plumbing (style is
      resolved at mount time through `Application.Current.Resources`, which
      re-evaluates on light/dark/contrast switches).
- [x] Unit test: applying `.AccentButton()` then `.SubtleButton()` keeps
      the last-write-wins behavior; `.ApplyStyle("...")` after either
      still overrides. (`NamedStyleFluentTests`.)

### 2.2 `.TextLink()` (§17.2)

- [x] `.TextLink()` on `HyperlinkButtonElement` and `ButtonElement` using
      `TextBlockButtonStyle`.
- [x] Sample: replace one inline `"Learn more"` `.ApplyStyle(...)` call in
      `samples/ReactorGallery/` with `.TextLink()` and confirm visual parity.
      (Landed in Phase 8.1 — `Learn more` button on `BasicInput/ButtonPage`.)

### 2.3 `InputScope` fluents (§17.3)

- [x] On `TextFieldElement`:
      `.NumericInput()`, `.EmailInput()`, `.UrlInput()`, `.PhoneInput()`,
      `.SearchInput()`. Map to the exact `InputScopeNameValue` members
      enumerated in spec §17.3.
- [x] Generic escape hatch: `.InputScope(InputScopeNameValue scope)` on
      `TextFieldElement`.
- [x] Scoped to `TextFieldElement` only — `PasswordBoxElement` would need
      a separate review of the WinUI behavior around password-input scopes
      (a `Password` input scope can disable the soft keyboard's password
      suggestion UI). Not in this PR.
- [x] Unit test: each fluent attaches a setter on `TextFieldElement.Setters`.
      (`NamedStyleFluentTests`.)

### 2.4 `InfoBar` severity fluents (§17.4)

- [x] `.Informational()`, `.Success()`, `.Warning()`, `.Error()` on
      `InfoBarElement`, mapping 1:1 to `InfoBarSeverity` members.
- [x] Unit test: each fluent flips the severity; last-write-wins.
      (`NamedStyleFluentTests`.)

### 2.5 `Card(Element child)` factory (§17.5)

- [x] Add `Card(Element child)` in `Factories.NamedStyles.cs` returning a
      preset `BorderElement` with: corner radius 8, padding 16,
      `CardBackgroundFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`
      1px stroke — all sourced via the existing `ThemeRef` /
      `Core.Theme` plumbing, not literals.
- [x] ~~Helper `ResolveCardBackground()`/`ResolveCardStroke()`~~ —
      not needed; `Background(ThemeRef)` / `WithBorder(ThemeRef)` already
      route through one place (`BrushHelper`).
- [x] Confirm chaining: `Card(child).Padding(24).CornerRadius(16)`
      override-wins. (`NamedStyleFluentTests`.)
- [ ] Light/Dark/HighContrast visual smoke under
      `tests/Reactor.AppTests/` (mount fixture, snapshot dimensions and
      resolved brush keys — not pixels). **Deferred** — requires the app
      test harness; tracked for Phase 11.4.

### 2.6 TextBlock type-ramp factories (spec §14 #6)

- [x] Add `Title`, `Subtitle`, `Body`, `BodyStrong`, `BodyLarge` factories
      alongside `Heading`/`SubHeading`/`Caption` in
      `Factories.NamedStyles.cs`, each returning a preset `TextBlockElement`
      with the WinUI 3 type ramp values
      (`TitleTextBlockStyle`, `SubtitleTextBlockStyle`,
      `BodyTextBlockStyle`, `BodyStrongTextBlockStyle`,
      `BodyLargeTextBlockStyle`).
- [x] **Do NOT also add fluents** for these — spec §17.6 explicitly warns
      against the two-ways trap. Confirmed: factories only.
- [x] Unit test: each factory attaches a mount action that applies the
      corresponding style. (`NamedStyleFluentTests`.)

---

## Phase 3: Missing events (spec §14 #3)

### 3.1 `CalendarView.OnSelectedDatesChanged` (Q4 from §15)

- [x] Decide the shape of the multi-selection callback. Spec Q4 OK's "a
      distinct API for CalendarView". Chose:
      `Action<IReadOnlyList<DateTimeOffset>>? OnSelectedDatesChanged` —
      snapshot of the full selection (not delta) so component state binds
      without diffing.
- [x] Add a `SelectedDates` init property (`IReadOnlyList<DateTimeOffset>?`)
      so initial selection is set declaratively, not via `.Set()`.
- [x] Wire the event in the `CalendarView` reconciler hook. Subscription
      is unconditional; the handler reads the latest element via the
      element-tag, and `SyncSelectedDates` uses `ChangeEchoSuppressor` to
      keep declarative reconciliation from echoing as user events.
- [x] Add `.SelectedDatesChanged(...)` / `.SelectedDates(...)` fluents
      (deferred from 1.3).
- [x] Unit test: null-clear (`EventFluentNullClearTests`). Reconciler-
      level "programmatic selection changes raise the callback exactly
      once per atomic change; no event during initial mount" deferred to
      AppTests harness (tracked in Phase 11.7).

### 3.2 `Frame.Navigated/.Navigating/.NavigationFailed`

- [x] Model the three events on `FrameElement` as Microsoft.UI.Reactor (Reactor)-shaped
      substitutes (matching the rest of the codebase, which doesn't
      surface raw WinUI EventArgs in user-facing callbacks):
      `Action<Type>? OnNavigated`, `Action<Type>? OnNavigating`,
      `Action<Type, Exception>? OnNavigationFailed`. Users who need
      `NavigationMode` / `NavigationParameter` / cancellation can wire
      the raw event via `.Set(...)`.
- [x] Fluent extensions: `.Navigated(...)`, `.Navigating(...)`,
      `.NavigationFailed(...)`.
- [x] Sample exercising `Frame.Navigated` for a navigation log under
      `samples/ReactorGallery/ControlPages/Navigation/`. (Landed in
      Phase 8.3 — `FrameNavigationPage` + three trivial Page-derived
      targets in `FrameSamplePages.cs`. All three fluents
      `.Navigating` / `.Navigated` / `.NavigationFailed` are wired
      into a 20-entry rolling log.)

### 3.3 `ScrollView.ViewChanged`

- [x] Add `OnViewChanged(Action<ScrollViewerViewChangedEventArgs>?)` to
      `ScrollViewElement`. Used the literal spec-shaped signature
      (`Action<EventArgs>?`, no sender) — `OnSizeChanged` does pass
      sender but Reactor users rarely need it; the args carry
      `IsIntermediate` which is the more useful debouncing hook.
- [x] Fluent `.ViewChanged(...)` + null-clear unit test.
- [x] Pooled-control wiring: subscribe-once via
      `PoolableWireFlags.ScrollViewerViewChanged`; the handler reads
      the live element via `GetElementTag` so a later
      record-with that attaches `OnViewChanged` picks up without
      re-wiring.
- [ ] AppTest: "triggering a scroll fires the callback with monotonic
      offsets" — deferred to Phase 11.7 (requires a WinUI runtime).

### 3.4 `Popup.Opened`

- [x] Add `OnOpened` to `PopupElement`; already has `OnClosed`. Fluent
      `.Opened(handler)` + null-clear test. Trampoline reads live
      element via the wrapper StackPanel's tag, matching the existing
      `OnClosed` pattern.

### 3.5 `WebView2.WebMessageReceived` / `CoreWebView2Initialized`

- [x] Add `OnWebMessageReceived(Action<string>?)` and
      `OnCoreWebView2Initialized(Action?)` to `WebView2Element`. Fluents
      `.WebMessageReceived(...)` / `.CoreWebView2Initialized(...)` +
      null-clear tests. Threading contract documented in XML doc
      comments (both fire on UI thread).
- [x] `OnWebMessageReceived` handler falls back from
      `TryGetWebMessageAsString()` to `WebMessageAsJson` so callers always
      receive a string payload even when the page sends a structured
      message via `postMessage(...)`.

### 3.6 `MediaPlayerElement.MediaOpened/.MediaEnded/.MediaFailed`

- [x] Wire the three events from the inner `MediaPlayer` to the element
      record. The MediaPlayer raises these on a worker thread; new
      helper `DispatchToElement<TElement>` marshals via the element's
      `DispatcherQueue.TryEnqueue` and resolves the live element via
      `GetElementTag` (handler is a no-op if the element is unmounted
      between fire and dispatch). Documented in XML doc comments.
- [x] `OnMediaFailed` receives the error message string
      (`args.ErrorMessage ?? args.Error.ToString()`) — keeps the
      callback signature simple while still preserving the most-useful
      failure detail.
- [x] Fluents `.MediaOpened(...)` / `.MediaEnded(...)` /
      `.MediaFailed(...)` + null-clear tests. Integration test
      ("fires-on-real-media") deferred to Phase 11.7 (AppTests harness).

### 3.7 Niche but documented gaps

Each item below is **[deferred]** to a follow-up spec (tracked in
`docs/specs/040-specialized-control-scrub.md`). Rationale: each surface
needs its own event-shape design pass (e.g. `MenuFlyout.Opening` exposes
a cancellable args object Reactor doesn't yet have a pattern for; `MapControl`
isn't packaged in this Reactor build at all). Bundling these into 039
would balloon the PR past review-budget without proportional user value.

- [x] **[deferred]** `MenuFlyout.Opening` / `MenuFlyout.Closing` — cancellable
      args (`Opening` exposes `CancelEventArgs`-shaped data); needs a
      Reactor-shaped cancellation pattern that doesn't exist elsewhere in
      the codebase yet. Tracked in spec 040.
- [x] **[deferred]** `CommandBar.IsOpenChanged` — `CommandBar` is itself
      niche in the current sample/app surface (no current consumer); add
      alongside any future `CommandBar` work. Tracked in spec 040.
- [x] **[deferred]** `SemanticZoom.ViewChangeStarted` /
      `SemanticZoom.ViewChangeCompleted` — `SemanticZoom` is not currently
      modelled as a Reactor element record; modelling the control is a
      prerequisite. Tracked in spec 040.
- [x] **[deferred]** `AnnotatedScrollBar.DetailLabelRequested` —
      `AnnotatedScrollBar` is a WinUI 3 1.5+ primitive not yet exposed as
      a Reactor element. Tracked in spec 040.
- [x] **[deferred]** `MapControl.ViewChanged` — `MapControl` lives in the
      Windows Maps SDK which is not a dependency of this Reactor build;
      out of scope for 039 entirely.

---

## Phase 4: Frequently-set init → fluent (spec §14 #2)

### 4.1 `Slider`

- [x] `.Orientation(Orientation)`, `.TickFrequency(double)`,
      `.TickPlacement(TickPlacement)`, `.SnapsTo(SliderSnapsTo)`,
      `.ThumbToolTip(bool enabled)` (spec §3.8).

### 4.2 `NumberBox`

- [x] `.NumberFormatter(INumberFormatter2)`,
      `.AcceptsExpression(bool = true)`,
      `.ValidationMode(NumberBoxValidationMode)`,
      `.Description(string)` (spec §3.3).

### 4.3 `ColorPicker`

- [x] `.AlphaEnabled(bool = true)`, `.MoreButtonVisible(bool = true)`,
      `.ColorSpectrumVisible(bool = true)`, `.ColorSliderVisible(bool)`,
      `.ColorChannelTextInputVisible(bool)`, `.HexInputVisible(bool)`
      (spec §3.11). One fluent per init property — all six.

### 4.4 `TabView`

- [x] `.TabWidthMode(TabViewWidthMode)`,
      `.CloseButtonOverlayMode(TabViewCloseButtonOverlayMode)`,
      `.CanDragTabs(bool = true)`, `.CanReorderTabs(bool = true)`,
      `.AllowDropTabs(bool = true)`, `.TabStripHeader(Element)`,
      `.TabStripFooter(Element)` (spec §7.4).

### 4.5 `NavigationView`

- [x] `.AutoSuggestBox(AutoSuggestBoxElement)`,
      `.PaneFooter(Element)`, `.PaneCustomContent(Element)`,
      `.OpenPaneLength(double)`,
      `.CompactModeThresholdWidth(double)`,
      `.ExpandedModeThresholdWidth(double)` (spec §7.2).

### 4.6 `TitleBar`

- [x] `.BackButtonVisible(bool)`, `.BackButtonEnabled(bool)`,
      `.PaneToggleButtonVisible(bool)`, `.Content(Element)`,
      `.RightHeader(Element)`, `.Icon(IconSource | string)`
      (spec §7.3).

### 4.7 `TextField`

- [x] `.MaxLength(int)`, `.IsSpellCheckEnabled(bool = true)`,
      `.CharacterCasing(CharacterCasing)`,
      `.TextAlignment(TextAlignment)`, `.Description(string)`
      (spec §3.1). `InputScope` lives in Phase 2.3.

### 4.8 Doc-comment fix for `HyperlinkButton(Command)` (spec §14 #5 + Q5)

- [x] Implement `.NavigateUri(Uri)` fluent on `HyperlinkButtonElement` — Q5
      decided to add the API, not just rewrite the doc.
- [x] Update the XML doc on `Button(Command)` / `HyperlinkButton(Command)`
      that promised `.NavigateUri(...)`. The promise is now real; just
      verify the wording matches actual behavior.

---

## Phase 5: Common-property gaps (spec §14 #4)

These are properties the spec called out by name as high-traffic gaps that
aren't covered by Phase 4 fluents. Some are net-new init properties on the
element record (with corresponding fluents); some are pure fluents over
existing init properties.

### 5.1 Text family

- [x] `TextBlockElement`: `LineHeight`, `MaxLines`, `CharacterSpacing`,
      `TextDecorations` as init properties + fluents (spec §1.1).
- [x] `RichTextBlockElement`: `MaxLines`, `LineHeight`, `TextAlignment`,
      `TextTrimming`, `CharacterSpacing` (spec §1.3).
- [x] `RichEditBoxElement`: `IsSpellCheckEnabled`, `MaxLength`,
      `TextWrapping`, `AcceptsReturn`, `SelectionHighlightColor` init
      + fluents (spec §1.4).

### 5.2 Buttons (spec §2 leftovers)

- [x] `ToggleButton`: nullable `IsChecked` + `IsThreeState` so a single
      record covers two/three-state, matching WinUI (spec §2.4). Verify the
      existing `ThreeStateCheckBox` precedent — does it split or unify?
      Match whichever pattern the rest of Reactor uses.
      Implemented as: added `IsThreeState`/`CheckedState`/`OnCheckedStateChanged`
      on `ToggleButtonElement` + `ThreeStateToggleButton(...)` factory,
      matching the existing `CheckBoxElement` / `ThreeStateCheckBox` precedent.

### 5.3 Input controls

- [x] `PasswordBoxElement`: `MaxLength`, `Header`, `PasswordRevealMode`,
      `PasswordChar` (spec §3.2).
- [x] `AutoSuggestBoxElement`: `Header`, `QueryIcon`,
      `IsSuggestionListOpen` (spec §3.4).
- [x] `ComboBoxElement`: `MaxDropDownHeight`, `Description`,
      `OnDropDownOpened`, `OnDropDownClosed` (spec §3.7).
- [x] `RatingControlElement`: `PlaceholderValue`, `InitialSetValue`;
      promote `Caption` to a fluent (spec §3.10).
- [x] `ColorPickerElement`: `ColorSpectrumShape`, `MinHue/MaxHue`,
      `MinSaturation/MaxSaturation`, `MinValue/MaxValue` (spec §3.11).

### 5.4 Date/time

- [x] `CalendarDatePicker`: `DateFormat`, `IsTodayHighlighted`,
      `IsCalendarOpen`, `IsGroupLabelVisible` (spec §4.1).
- [x] `DatePicker`: `DayFormat`, `MonthFormat`, `YearFormat`,
      `Orientation` (spec §4.1).
- [x] `CalendarView`: `MinDate`, `MaxDate`, `FirstDayOfWeek`,
      `NumberOfWeeksInView`, `DisplayMode` (spec §4.1; `SelectedDates*`
      handled in Phase 3.1).

### 5.5 Progress, layout, navigation

- [x] `InfoBarElement`: `IconSource`, `Content` (Element child)
      (spec §5).
- [x] `WrapGridElement`: attached-prop fluents
      `.WrapGridColumnSpan(int)` / `.WrapGridRowSpan(int)` (spec §6).
- [x] `ExpanderElement`: `HeaderTemplate` (Element-typed slot),
      `ContentTransitions` (spec §6).
- [x] `SplitViewElement`: `PaneBackground` (Brush or theme-resource key),
      `LightDismissOverlayMode` (spec §6).

### 5.6 Collections, dialogs

- [x] `ListView`/`GridView`: `ItemContainerStyle`,
      `IncrementalLoadingTrigger`. (deferred: `GroupStyle` — needs a
      Reactor-shaped wrapper; tracked separately.)
- [x] `ContentDialogElement`: `IsPrimaryButtonEnabled`,
      `IsSecondaryButtonEnabled`, `OnOpened`, `OnOpening` (spec §9).
      Landed `IsPrimaryButtonEnabled`, `IsSecondaryButtonEnabled`, and
      `OnOpened` (with `.Opened()` fluent). WinUI 3 `ContentDialog`
      doesn't expose an `Opening` event — `OnOpening` deferred upstream.
- [x] `FlyoutElement`: `ShowMode`, `AreOpenCloseAnimationsEnabled`,
      `OverlayInputPassThroughElement` (spec §9).
- [x] `TeachingTipElement`: `IconSource`, `HeroContent`, `PlacementMargin`,
      `PreferredPlacement` (spec §9).

### 5.7 Media + shapes

- [x] `ImageElement`: `OnImageOpened`, `OnImageFailed`, `NineGrid`
      (spec §10).
- [x] `WebView2Element`: surface `Source` as a non-init-only init
      property (re-init via record-`with`) (spec §10).
      Already covered: `Source` is the record's positional parameter
      and `UpdateWebView2` reconciles it across renders.
- [x] `PathElement`: `StrokeStartLineCap`, `StrokeEndLineCap`,
      `StrokeLineJoin`, `StrokeMiterLimit`, `StrokeDashCap`,
      `StrokeDashOffset`, `FillRule` (spec §11). `FillRule` lives on
      `PathGeometry`, not `Shapes.Path`, so the reconciler propagates
      it through `p.Data as PathGeometry`.
- [x] `PipsPagerElement`: `WrapMode`, `MaxVisiblePips`,
      `PreviousButtonVisibility`, `NextButtonVisibility` (spec §12).
- [x] `RefreshContainerElement`: `PullDirection` (spec §12).
- [x] `ParallaxViewElement`: `Source`, `VerticalSourceStartOffset`/
      `EndOffset` (spec §12).

### 5.8 Universal collection `SelectionChanged`

- [x] Decide the multi-selection event shape for `ListView`/`GridView`/
      `ListBox`/`TreeView`. Spec §8 calls this out as a "universal gap" and
      a follow-up. If a typed `IReadOnlyList<int>` (or `IReadOnlyList<T>`
      for typed peers) callback is acceptable, add it; otherwise punt to a
      tracked deferral with a checkbox `[deferred]`.
      Landed snapshot-style `OnSelectionChanged`:
      - `ListView` / `GridView` / `ListBox` receive `Action<IReadOnlyList<int>>?`
      - `TemplatedListView<T>` / `TemplatedGridView<T>` / `ItemsView<T>` receive
        `Action<IReadOnlyList<T>>?` (typed peers materialize items from indices)
      - `TreeView` (deferred): hierarchical selection model needs separate design
      Snapshot semantics match the CalendarView `OnSelectedDatesChanged`
      decision from Phase 3.1.

---

## Phase 6: Naming alignment (spec §0.4 + §14 #8 + §16)

### 6.1 `RichText` → `RichTextBlock` (spec §14 #8)

- [x] Rename `RichText` factory + `RichTextBlockElement` references so
      factory + record + WinUI name align. Keep `RichText` as an
      `[Obsolete]` forwarding alias for one release per the §16.4 (B)
      pattern. (Record was already `RichTextBlockElement`; rename was
      factory-only: both `RichText(string)` and `RichText(RichTextParagraph[])`
      now live as `RichTextBlock(...)` with thin `[Obsolete]` forwarders.)
- [x] Update the ~ten sample / doc / test call sites to the new name.
- [x] Add a CHANGELOG entry under the breaking-change-deferred section.
      (Created new `### Breaking changes (deferred)` section under
      `## [Unreleased]` in `CHANGELOG.md`.)

### 6.2 Document Reactor-original wrappers (spec §0.3)

- [x] Add XML doc comments to `VStack`/`HStack`/`Heading`/`SubHeading`/
      `Caption`/`Flex`/`FlexRow`/`FlexColumn`/`LazyVStack<T>`/
      `LazyHStack<T>`/`TemplatedListView<T>` explaining why the name
      diverges from WinUI. Also covered the sibling templated peers
      `GridView<T>` and `FlipView<T>` and the second `Flex(direction, ...)`
      overload. (`<summary>` + `<remarks>` per factory in `Dsl.cs`.)

### 6.3 Document the `StackElement.Spacing` default-8 deviation (spec §0.4)

- [x] Add an XML doc remark on `StackElement.Spacing` calling out the
      default-8 vs WinUI's default-0. Doc-only — default unchanged.

### 6.4 `TextField` rename — NOT in scope

- [x] Confirm spec §16.5 decision (don't rename) is reflected in code by
      adding a one-line XML doc on the `TextField` factory pointing readers
      to §3.1 / §16 in the spec for naming rationale.
- [x] Same for `MaskedTextField` (spec §13 / §16.3).

### 6.5 `ScrollView` vs `ScrollViewer` (spec §6 naming notes)

- [x] Decision: **keep `ScrollView`. No `ScrollViewer` factory alias.**
      Reactor's `ScrollViewElement` reconciles to WinUI's legacy `ScrollViewer`,
      but WinUI 3 also ships the newer `Microsoft.UI.Xaml.Controls.ScrollView`
      — the shorter, modern name. Reactor's `ScrollView` is already
      consistent with that preferred WinUI name.
      **Originally added an `[Obsolete]` `ScrollViewer` forwarding alias
      for discoverability, but the PR #314 review (Copilot) flagged that
      the alias is *not* purely additive: under `using static
      Microsoft.UI.Reactor.Factories;` alongside `using
      Microsoft.UI.Xaml.Controls;`, the bare name `ScrollViewer` then
      shadows the WinUI type, so any existing code calling its attached
      properties (`ScrollViewer.SetVerticalScrollMode(...)` etc.) has to
      fully-qualify with `global::Microsoft.UI.Xaml.Controls.ScrollViewer.`
      The discoverability win didn't justify the imposed disambiguation
      cost on existing consumers — alias removed.**

### 6.6 `Progress` vs `ProgressBar` (spec §5)

- [x] Decision: **keep `Progress` / `ProgressIndeterminate`, add
      `[Obsolete]` `ProgressBar` / `ProgressBar()` aliases.** Reactor
      names by intent (`Progress`) rather than by rendering shape
      (`ProgressBar` includes the visual primitive in the name).
      Aliases let WinUI muscle-memory callers discover the Reactor
      spelling without breaking code.

---

## Phase 7: Specialized Reactor controls (spec §13)

This phase is intentionally scoped — the spec defers full coverage to
**spec 040**, but small follow-on items belong here.

### 7.1 Schedule spec 040

- [x] Create `docs/specs/040-specialized-control-scrub.md` as a draft with
      the same three-checks-per-control structure as 039, applied to
      `AutoSuggestElement<T>`, `DataGridElement<T>`, `MaskedTextFieldElement`,
      `PropertyGridElement`, `VirtualListElement`. (Draft scaffold only —
      Status: Draft — does not pre-audit the controls.)

### 7.2 Quick-win events for specialized controls

- [x] Inventory specialized controls' callback properties using the same
      script from 0.3 (extend it to cover `src/Reactor/Controls/**`).
      (The script already globs `src/Reactor/Controls/**`. Re-ran and
      regenerated `tests/Reactor.SelfTests/Fixtures/event-fluent-inventory.csv`.
      Also broadened the delegate-type regex to capture one level of
      nested generics so `Action<IReadOnlySet<RowKey>>?` on
      `DataGridElement<T>` is now picked up.)
- [x] Land fluent extensions for any callback that does not already have
      one. Phase 0.4 (source generator) was struck (hand-written chosen),
      so this is purely a hand-written sweep.
      - `AutoSuggestElement<T>.OnSelected` — covered (Phase 1).
      - `MaskedTextFieldElement.OnChanged` — covered (Phase 1).
      - `PropertyGridElement.OnRootChanged` — covered (Phase 1).
      - `VirtualListElement.OnVisibleRangeChanged` — covered (Phase 1).
      - `DataGridElement<T>.OnSelectionChanged` — **new** fluent
        `.SelectionChanged(...)` added; null-clear test added.

---

## Phase 8: Samples

Samples document the new surface in-tree and double as smoke tests.

### 8.1 ReactorGallery — fluent showcases

- [x] In `samples/ReactorGallery/ControlPages/BasicInput/`, add a sample
      page or update an existing one to use `Button("Save").Click(...)`
      / `.AccentButton()` / `.SubtleButton()` / `.TextLink()`. (Phase 1
      renamed the fluent to drop the leading `On` — `.Click`, not
      `.OnClick`. The underlying property remains `OnClick`.)
- [x] In `BasicInput/TextField`, demo `.NumericInput()`/`.EmailInput()`/
      `.UrlInput()` next to a `Description("...")` fluent.
- [x] In `StatusAndInfo/InfoBar`, demo all four severity fluents.
- [x] In `Text`, add a "Type ramp" page using `Title`/`Subtitle`/`Body`/
      `BodyStrong`/`BodyLarge`. (New page `Text/TypeRampPage.cs`
      registered in `ControlRegistry` / `PageRouter`.)
- [x] In `Layout`, add a `Card(child)` example with three nested children
      (icon, heading, body) so the theme-aware resolution is visible.
      (New page `Layout/CardPage.cs` registered.)
- [x] In `DateAndTime/CalendarView`, demo
      `.SelectedDatesChanged(...)` + multi-select. (Fluent drops the
      leading `On`; property remains `OnSelectedDatesChanged`.)

### 8.2 Apps that already exhibit gaps

- [x] `samples/apps/chat`: replace any `.Set(c => c.OnClick = ...)` with
      the new `.Click(...)` fluent. Grep first; no-op if none found.
      (No-op — grep showed zero matches. The chat app's `.Set(...)`
      calls are all theme-resource templating (`.Set("ButtonBackground", ...)`)
      or style-only WinUI knobs (`Padding`, `TextWrapping`, etc.),
      none of them callback properties.)
- [x] `samples/apps/demo-script-tool`: same sweep; replace `.Set()`
      escape-hatches that are now expressible with fluents.
      (No-op — only `.Set("ButtonForeground...", ThemeRef(...))` hits.)
- [x] `samples/apps/regedit` / `wordpuzzle` / `validation-showcase` /
      `a11y-showcase`: same sweep. (All four: no-op. Property-init
      `OnClosed = ...` / `OnItemInvoked = ...` patterns in regedit are
      already in `with { ... }` record-construction blocks, not inside
      `.Set(...)` escape-hatches, so the fluent swap doesn't apply.)

### 8.3 New micro-sample for `Frame` navigation events

- [x] Under `samples/ReactorGallery/ControlPages/Navigation/`, demonstrate
      `Frame.Navigated`/`.Navigating`/`.NavigationFailed` with a log
      panel showing each event. (Fluents drop the leading `On`; the
      underlying properties remain `OnNavigated` etc.) Landed as
      `Navigation/FrameNavigationPage.cs` + `Navigation/FrameSamplePages.cs`.

---

## Phase 9: Docs

### 9.1 Guide updates

- [x] `docs/guide/getting-started.md`: replace any `.Set(c => c.OnClick = ...)`
      examples with the new fluent. (No-op — the guide already uses the
      `Button("…", onClick)` factory shape throughout; grep confirmed zero
      `.Set(c => c.OnX = ...)` and zero `with { OnX = ... }` patterns.)
- [x] `docs/guide/forms.md`: cover `.NumericInput()` / `.EmailInput()` /
      `.MaxLength` etc. as the canonical TextField configuration story.
      (New "Configuring TextField" section with worked example + lookup
      table covering Phase 2.3 named-input shapes + Phase 4.7 properties.)
- [x] `docs/guide/styling.md`: add a section on named-style fluents
      (`AccentButton`, `SubtleButton`, `TextLink`, severity helpers).
      (New "Named-Style Fluents" section sits before the existing
      "Lightweight Styling" block.)
- [x] `docs/guide/layout.md`: add a `Card(child)` example and the
      type-ramp factories. (Two new sections: "Card" and "Type-Ramp
      Factories", landed before "ScrollView and Border".)
- [x] `docs/guide/navigation.md`: cover `Frame` events,
      `NavigationView.OnSelectedTagChanged` fluent. (New sections
      "Frame Events" and "NavigationView.SelectedTagChanged" land just
      before "Navigation Diagnostics".)
- [x] `docs/guide/collections.md`: cover any new collection events from
      Phase 5.8. (New "Multi-Select with SelectionChanged" section
      covers ListView / GridView / ListBox + typed peers; calls out the
      `IReadOnlyList<int>` vs `IReadOnlyList<T>` signature split and the
      intentional TreeView deferral.)

### 9.2 Reference docs

- [x] If `docs/reference/` contains an auto-generated API surface, regenerate.
      (No auto-gen system found under `docs/reference/` — the only
      machine-derived API surface is `skills/reactor.api.txt` and its
      mirror at `plugins/reactor/skills/reactor-dsl/references/reactor.api.txt`,
      both regenerated on every Reactor build. No manual action needed
      here; agent-kit regen is Phase 10.1's scope.)
- [x] `docs/guide/xaml-developers.md`: explicitly contrast `OnClick`
      attribute (XAML) → `.OnClick(handler)` (Reactor fluent) so XAML
      developers find the bridge. (New "Events: XAML Attributes Become
      Fluents" section with the canonical mapping table for `Click`,
      `TextChanged`, `SelectionChanged`, `SelectedIndexChanged`,
      `IsCheckedChanged` + the property-vs-fluent naming note.)

### 9.3 Spec backlinks

- [x] In `docs/specs/039-property-and-event-scrub.md`, append a "Status:
      Implemented" header pointing at this task list and the merge PR(s).
      (Header replaced; backlinks to this task list and the per-phase
      commit history via `git log feat/039-property-event-scrub --grep='Phase '`.)

### 9.4 CHANGELOG

- [x] Add entries to `CHANGELOG.md` grouped: "New fluent extensions" (one
      bullet listing the ~60), "Named-style helpers", "New events
      exposed", "Naming aliases (`RichText` → `RichTextBlock`)".
      (Single nested bullet block under a new `### Added` heading in
      `[Unreleased]` covering all four buckets — Naming aliases stayed
      in the existing `Breaking changes (deferred)` block where Phase
      6.1 originally landed them.)

---

## Phase 10: Agent-kit skills (`plugins/reactor/`)

The agent-kit is how AI coding assistants discover Reactor APIs. Every new
fluent must surface here or it doesn't exist for agents.

### 10.1 `reactor-dsl` skill

- [x] Regenerate `plugins/reactor/skills/reactor-dsl/references/reactor.api.txt`
      from the new public surface so all `On...`, named-style, and new
      init-property fluents are listed. (Auto-regenerated by
      `tools/Reactor.SignaturesGen` on every Reactor build; current copy
      is up-to-date — `dotnet build src/Reactor/Reactor.csproj` produced
      no diff. Note: only the `plugins/reactor/skills/reactor-dsl/references/`
      copy is updated by the build; the legacy `skills/reactor.api.txt`
      mirror is not auto-regenerated — Phase 12 should reconcile.)
- [x] Update `plugins/reactor/skills/reactor-dsl/SKILL.md` if it documents
      naming conventions for fluents — clarify that callback properties
      have matching `.OnX()` extensions. (New "Fluent naming convention
      (callbacks)" section explains the `OnX` property → `.X(...)` fluent
      drop, the C# clash rationale, and the null-clear semantic.)

### 10.2 `reactor-recipes` skill

- [x] Update existing recipes to use the new fluents where they currently
      use `.Set(c => c.OnX = ...)`:
      - `themed-card.cs` — now uses the `Card(child)` factory + type-ramp
        `Subtitle`/`Caption` for headings.
      - `form-with-validation.cs` — adds an Age field demonstrating
        `.NumericInput()` + `.MaxLength(3)`, applies `.EmailInput()` to
        the email field, header note covers the `.Changed(...)` fluent
        and its replace-vs-append semantic.
      - `list-add-delete.cs` — converted from manual VStack to templated
        `ListView<T>(items, key, builder).ItemClick(...)`; per-row keying
        is now factory-supplied.
      - `sidebar-nav.cs` — header note + inline comment explain the
        `.SelectedTagChanged(...)` fluent and why `.WithNavigation` and
        `.SelectedTagChanged` are mutually exclusive on the same element.
- [x] Add new recipe `plugins/reactor/skills/reactor-recipes/references/named-styles.cs`
      showing `AccentButton`/`SubtleButton`/`TextLink`/severity fluents
      side-by-side.
- [x] Add new recipe `plugins/reactor/skills/reactor-recipes/references/calendar-multiselect.cs`
      demonstrating `OnSelectedDatesChanged`.
- [x] Update `plugins/reactor/skills/reactor-recipes/references/index.md`
      to reference the new recipes.
- [x] Mirror the changes in the legacy `skills/recipes/` folder if it's
      still consumed (grep references; deprecate if not). (Legacy folder
      IS still consumed — `src/Reactor/Reactor.csproj` lines 99–102 pack
      `skills/*.md` and `skills/recipes/*.{md,cs}` to `agentkit/skills/`
      in the NuGet. All four updated recipes + both new recipes copied
      across; legacy `index.md` updated. Phase 12 should reconcile the
      duplicate-layout source-of-truth question.)

### 10.3 `reactor-design` skill

- [x] If `reactor-design` references the typography ramp or card pattern,
      update it to point at `Title`/`Subtitle`/`Body`/`Card(...)` instead
      of `.Set()` recipes. (Typography table extended with the WinUI 3
      ramp factories `Title`/`Subtitle`/`Body`/`BodyStrong`/`BodyLarge`
      with note disambiguating from the older `Heading`/`SubHeading`
      Reactor presets; the "Avoid Deep Nesting" example gains a `Card(...)`
      one-liner. Mirror update applied to legacy `skills/design.md`.)

### 10.4 `reactor-getting-started` skill

- [x] Audit the SKILL.md for any `.Set(c => c.OnClick = ...)` usage; swap
      for fluent. (No-op: grep showed zero `.Set(c => c.On*)` or
      `with { On* = ... }` patterns in
      `plugins/reactor/skills/reactor-getting-started/SKILL.md`.)

### 10.5 `reactor-forms` skill

- [x] Audit for `TextField` / `NumberBox` / `PasswordBox` examples; swap
      for `.NumericInput()`/`.EmailInput()`/`.MaxLength()` etc. (Available
      modifiers table extended for `TextField` and `PasswordBox`; new
      "Named-input shapes" callout with a worked `.EmailInput()` +
      `.MaxLength()` + `.Validate()` chain.)

### 10.6 `reactor-commanding`, `reactor-navigation`, `reactor-input` skills

- [x] Audit for newly-fluent events (`.OnClick`, `.OnSelectedTagChanged`,
      `.OnRefreshRequested`); swap from the property-init pattern.
      (No-op for all three: grep showed zero `.Set(c => c.On*)`,
      `with { On* = ... }`, or property-init callback patterns in any of
      `reactor-commanding`, `reactor-navigation`, `reactor-input` SKILL.md
      files. The reactor-navigation skill talks about `UseNavigationLifecycle`
      hook callbacks, not raw NavigationView events.)

### 10.7 Top-level `SKILL.md` and `skills/dsl-reference.md`

- [x] Update if it lists factories — add type-ramp factories and `Card`.
      (Root `SKILL.md` cheatsheet: type-ramp `Title`/`Subtitle`/`Body`/
      `BodyStrong`/`BodyLarge` line added next to the existing
      `Heading`/`SubHeading`, plus a `Card(child)` line above the manual
      `Border(child).Background(Theme.CardBackground)...` example.
      Recipe roll-call now lists `canvas-positioning`, `named-styles`,
      `calendar-multiselect`. `skills/dsl-reference.md` typography table
      gains the five WinUI ramp factories; layout-containers table gains
      a `Card(child)` row.)
- [x] Update `skills/forms.md` / `skills/input.md` / `skills/navigation.md`
      mirrors of the items in 10.5 / 10.6. (`skills/forms.md` available-
      input-types table extended for `TextField` and `PasswordBox` to
      match the `reactor-forms` update. `skills/input.md` /
      `skills/navigation.md` / `skills/commanding.md`: no-op — same
      grep-clean result as 10.6.)

---

## Phase 11: Tests

Most phases above include their own targeted tests; this phase covers the
cross-cutting test surface.

### 11.1 Public-API surface test

- [x] Add a self-test under `tests/Reactor.SelfTests/` that walks every
      `Element` record reflectively and asserts:
      (a) every `Action`/`Action<T>` property has a matching public
      extension method whose name equals the property name and whose
      first parameter is the element type;
      (b) the extension exists in `ElementExtensions.Events.cs` or is
      source-generated. This guards against future records adding a
      callback without a fluent.
      (Landed as xUnit `PublicApiSurfaceGuardTests.EveryCallbackPropertyHasMatchingFluent`
      under `tests/Reactor.Tests/Elements/` — Reactor.SelfTests is a
      Host-launcher MSTest harness, so the reflective surface test lives
      with the rest of the element-record unit suites. Two intentional
      property-name exceptions documented inline: `VirtualListElement.Ref`
      (ref-capture clashes with the generic `.Ref<T>` modifier) and
      `XamlHostElement.Updater` (constructor-time interop hook, not an
      event). All other `OnX` callbacks resolve to a matching fluent.)

### 11.2 Naming-alignment guard test

- [x] Self-test that asserts factories with names diverging from their
      WinUI target (the §0.3 list) carry an XML doc remark explaining the
      deviation. Use the existing doc-comment extraction tooling (grep
      under `tests/Reactor.SelfTests/` for `// doc` precedent).
      (Landed as `NamingAlignmentGuardTests.DivergentFactoriesHaveRemarks`
      under `tests/Reactor.Tests/Elements/` — loads `Reactor.xml` (emitted
      next to `Reactor.dll` by `GenerateDocumentationFile`) and asserts
      each of `VStack`/`HStack`/`Heading`/`SubHeading`/`Caption`/`Flex`/
      `FlexRow`/`FlexColumn`/`LazyVStack`/`LazyHStack`/`ListView<T>` has at
      least one overload with a non-empty `<remarks>`. The generic
      `ListView<T>` overload-only match captures the typed-vs-untyped
      divergence without flagging the WinUI-spelling untyped `ListView`.)

### 11.3 Null-clear contract test

- [x] Combined with 1.6 — confirm every new `OnX(...)` fluent treats `null`
      as a clear-handler operation.
      (Audited `EventFluentNullClearTests.cs` against the CSV inventory;
      added 8 missing facts: `ItemsView.ItemInvoked`, `ItemsView.SelectionChanged`,
      `TemplatedGridView.ItemClick/SelectedIndexChanged/SelectionChanged`,
      `TemplatedFlipView.SelectedIndexChanged`, `TemplatedListView.SelectionChanged`,
      and `PropertyGrid.RootChanged`. Total now 92 facts.)

### 11.4 Theme-resource resolution test for `Card` and named-style buttons

- [x] Switch theme between Light / Dark / HighContrast and confirm the
      `Card` background, stroke, and `.AccentButton()` brush re-resolve.
      (Unit-level smoke landed as `CardThemeResolutionSmokeTests` under
      `tests/Reactor.Tests/Elements/` — asserts `Card(child)` wires the
      `CardBackgroundFillColorDefaultBrush` / `CardStrokeColorDefaultBrush`
      keys into `ThemeBindings`, cross-checks against `Theme.CardBackground`
      / `Theme.CardStroke`, and asserts `.AccentButton()` stores an OnMount
      action whose captured closure references `AccentButtonStyle`. The
      full Light/Dark/HighContrast flip-and-verify-brush test requires a
      theme-flip primitive on `Reactor.AppTests`, which doesn't exist
      today — **deferred** as Reactor.AppTests follow-up.)
- [ ] **Deferred — Reactor.AppTests follow-up**: real theme-flip test that
      mounts a Card under each of Light / Dark / HighContrast and asserts
      the resolved `Background` / `BorderBrush` `SolidColorBrush.Color`
      values differ across the three themes. Blocked on adding a theme-flip
      primitive to the AppTests harness.

### 11.5 Snapshot tests for generated fluents (if Phase 0.4 went source-gen)

- [x] One snapshot per element with callbacks; update on schema change.
      (No-op: source-gen scaffolding struck per Phase 0.4 / Q1; the
      fluents are hand-written so the reflective surface guard from 11.1
      already provides the equivalent drift detection.)

### 11.6 Sample smoke (gallery and apps)

- [x] `tests/Reactor.AppTests/` — for every sample touched in Phase 8,
      mount under Light/Dark/NightSky and confirm no exceptions and the
      bound callbacks fire.
      (Pragmatic landing as `GallerySampleConstructionSmokeTests` under
      `tests/Reactor.Tests/Elements/` — one fact per Phase-8 page asserting
      the page's factory + fluent chain constructs without exception and
      that its bound callbacks reach the expected element property. Pages
      are `internal` types in a WinExe, so importing them whole-cloth would
      be heavyweight; the fixture replicates the spec-039-flagged factory
      and fluent surface each page exercises. Full Light/Dark/NightSky
      mount-and-fire test deferred — needs Reactor.AppTests harness.)
- [ ] **Deferred — Reactor.AppTests follow-up**: real page-mount tests
      that load each Phase-8 page under Light / Dark / NightSky and
      simulate the bound callbacks firing. Blocked on a page-mount
      primitive (and a theme-flip primitive) in the AppTests harness.

### 11.7 Regression for the spec-flagged bugs

- [x] `AutoSuggestBox.OnSuggestionChosen` (spec §3.4) — assert reachable
      via factory **and** fluent.
      (Landed as `SpecFlaggedBugRegressionTests.AutoSuggestBox_*` —
      factory path covered via the record positional constructor (the
      4th positional parameter), fluent path covered via
      `.SuggestionChosen(h)`, plus a fluent-overrides-constructor check.)
- [x] `HyperlinkButton.NavigateUri` (spec §14 #5) — assert the fluent
      exists and the doc comment promise is fulfilled.
      (Named regression checkpoint under `SpecFlaggedBugRegressionTests`;
      canonical value-set tests already live in `Phase4InitFluentTests`.)
- [x] `Card(child)` smoke — assert resolved brushes match the theme dict.
      (Named regression checkpoint under `SpecFlaggedBugRegressionTests`;
      detailed wiring lives in `CardThemeResolutionSmokeTests` from 11.4.)

---

## Phase 12: Ship-readiness gates

### 12.1 Build & analyzer clean

- [x] `dotnet build Reactor.slnx -warnaserror` clean on a fresh clone.
      (Verified after rebase onto `origin/main` — picks up the
      "make solution warning-free" upstream fix (#311) which cleared a
      pre-existing `NU1903` Nerdbank.MessagePack vulnerability warning
      that was independent of the 039 surface.)
- [x] `REACTOR_A11Y_001..003` clean across all touched files. (Implied by
      `-warnaserror` clean — the analyzers run during build.)
- [x] `CS1591` clean — every new public surface has XML doc comments.
      (Implied by `-warnaserror` clean.)

### 12.2 Perf neutrality

- [x] Run `tests/startup_perf/` baseline; assert no regression vs main
      beyond the existing noise floor.
      **Result (5-run median, ARM64 Release, foregrounded, AC):**
      Reactor TTFP 238.1 ms / TTI 244.0 ms / PeakWS 118.1 MB.
      Calibrated pre-039 baseline (same machine, May 6): TTFP 230.2 ms /
      TTI 234.6 ms / PeakWS 113.1 MB. ~3% absolute drift, well within the
      run-to-run noise floor (WinUI3 baseline moved 178 → 147 ms TTFP in
      the *opposite* direction over the same window, confirming
      system-level variance dominates). Output:
      `tests/startup_perf/out_039_after/`.
- [x] Run `tests/stress_perf/` once (foregrounded — per the memory
      [[reference_stress_perf_window_throttling]] — and on AC power per
      [[reference_stress_perf_drr_battery]]) to confirm no regression.
      **Result (Reactor + ReactorOptimized @ 50% / 100%, ARM64 Release,
      foregrounded, AC, `-SkipETW`):** Reactor reconcile times are
      essentially identical to the May-10 baseline (Reactor 50%: 46.3 ms
      → 46.7 ms; Reactor 100%: 56.9 ms → 56.8 ms). The ~9% FPS delta
      tracks framework-overhead noise outside Reactor's hot path —
      consistent with the API-additive nature of 039 (no reconciler /
      diff / mount-path code touched). Output:
      `tests/stress_perf/baselines/full-matrix-2026-05-16-071025/`.

### 12.3 PR description checklist

- [ ] Lists every spec-§ entry resolved with a status checkbox.
- [ ] Lists every spec-§ entry deferred with a tracked follow-up.
- [ ] Lists the two `[Obsolete]` aliases (`RichText`, potentially
      `ScrollView`/`Progress`) and their planned removal release.
- [ ] Confirms agent-kit + skills + samples landed in the same PR.

### 12.4 Release notes

- [ ] One paragraph for the README highlights, one section in
      `CHANGELOG.md`, one update in `docs/guide/readme.md`.

---

## Tracking

Cross-cutting tracker — check these off as a coarse-grained progress signal
that maps to the spec's §14 ordering:

- [x] §14 #1 — Fluent for every callback (Phase 1 — extensions drop the
      leading `On` per the C# clash discovered in Phase 0.1).
- [x] §14 #2 — High-traffic init→fluent promotion (Phase 4).
- [x] §14 #3 — Missing events modelled (Phase 3; 3.7 niche events
      `[deferred]` to spec 040 with per-item rationale).
- [x] §14 #4 — Common-property gaps (Phase 5; GroupStyle and TreeView
      multi-select deferred — see Phase 5.6 / 5.8 notes).
- [x] §14 #5 — `HyperlinkButton(Command)` doc comment + `.NavigateUri()`
      (Phase 4.8).
- [x] §14 #6 — Type-ramp factories (Phase 2.6).
- [x] §14 #7 — Spec 040 scheduled for specialized controls (Phase 7.1).
- [x] §14 #8 — `RichText` → `RichTextBlock` rename (Phase 6.1).
- [x] §17 — Named-style fluents and `Card` factory (Phase 2).
- [x] Samples updated (Phase 8).
- [x] Docs updated (Phase 9).
- [x] Agent-kit updated (Phase 10).
- [x] Tests landed (Phase 11).
- [ ] Ship gates green (Phase 12).
