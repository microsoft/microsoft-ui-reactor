# Forms & Data Entry — Validation Implementation Tasks

Derived from: `docs/proposals/forms-data-entry-ideas.md`

---

## Phase 1: Validation Core (1A–1E)

### 1A. Validation Context & Producers (3–4 weeks)

#### 1A.1 ValidationMessage Record
- [x] Create `Reactor/Validation/ValidationMessage.cs`
- [x] Define `Severity` enum: `Error`, `Warning`, `Info`
- [x] Define `ValidationMessage` record:
  - [x] `string Field`
  - [x] `string Text`
  - [x] `Severity Severity` (default `Severity.Error`)
  - [x] `string? Code` (machine-readable, for i18n/dedup)
- [x] Unit tests: record equality, `with` expressions, default severity

#### 1A.2 ValidationContext Core
- [x] Create `Reactor/Validation/ValidationContext.cs`
- [x] Implement message collection: `Add(string field, string text, Severity severity = Error)`
- [x] Implement message clearing: `Clear(string field)`, `ClearAll()`
- [x] Implement query methods:
  - [x] `IReadOnlyList<ValidationMessage> GetMessages(string field)`
  - [x] `IReadOnlyList<ValidationMessage> GetAllMessages()`
  - [x] `bool HasError(string field)`
  - [x] `bool HasMessages(string field)`
  - [x] `Severity? HighestSeverity(string field)`
  - [x] `bool IsValid()` — true when no Error-severity messages
  - [x] `IReadOnlyList<string> InvalidFields` — fields with Error messages
- [x] Unit tests: add/query/clear messages, `IsValid()` logic, `InvalidFields` population
- [x] Unit tests: multiple messages per field, mixed severities

#### 1A.3 UseValidationContext Hook
- [x] Create `Reactor/Validation/UseValidationContext.cs` (or integrate into existing hook infrastructure)
- [x] Implement `UseValidationContext()` hook that provides/creates a `ValidationContext` scoped to the current component tree
- [x] Support nested contexts (child visualizer creates child scope that can bubble to parent)
- [x] Unit tests: hook returns same context within same scope, child scope creation

#### 1A.4 Built-in Validators
- [x] Create `Reactor/Validation/Validators/` directory
- [x] Implement `IValidator` interface (or `Func<T, ValidationMessage?>` delegate pattern)
- [x] Implement validators:
  - [x] `Required()` — non-null, non-empty, non-default
  - [x] `MinLength(int n)` — string length >= n
  - [x] `MaxLength(int n)` — string length <= n
  - [x] `Range(double min, double max)` — numeric bounds
  - [x] `Match(string regex)` — regex pattern match
  - [x] `Email()` — email format (built-in regex)
  - [x] `Url()` — URL format
  - [x] `Must(Func<T, bool> predicate, string message)` — custom sync lambda
  - [x] `MustAsync(Func<T, Task<bool>> predicate, string message)` — custom async lambda
  - [x] `MustBeTrue(string message)` — bool must be true (checkbox)
  - [x] `EqualTo(T otherValue, string message)` — cross-field equality
- [x] Unit tests per validator: valid input passes, invalid input returns correct message
- [x] Unit tests: validator composition (chaining multiple validators)
- [x] Unit tests: custom `Must()` lambda with complex predicates

#### 1A.5 `.Validate()` Extension Method
- [x] Create `Reactor/Validation/ValidateExtensions.cs`
- [x] Implement `.Validate(params IValidator[] validators)` extension on Microsoft.UI.Reactor (Reactor) element builder types
- [x] Wire `.Validate()` to register the control + validators with the nearest `ValidationContext`
- [x] Run validators on every render/state change, push results to context
- [x] Handle async validators: debounce, cancellation of in-flight async checks
- [x] Unit tests: `.Validate()` on TextField, NumberBox, CheckBox
- [x] Unit tests: validators run on value change, messages appear in context
- [x] Unit tests: async validator debounce and cancellation

#### 1A.6 Cross-Field ValidationRule
- [x] Create `Reactor/Validation/ValidationRule.cs` (element or component)
- [x] Implement `ValidationRule(Func<bool> predicate, string message, string field)` element
- [x] Place anywhere in tree; errors bubble to nearest visualizer
- [x] Support async variant: `ValidationRule(Func<Task<bool>> predicate, ...)`
- [x] Unit tests: cross-field rule fires when predicate fails
- [x] Unit tests: cross-field rule clears when predicate passes
- [x] Unit tests: rule associated with correct field name

#### 1A.7 Async/External Producer Support
- [x] Extend `ValidationContext` to support external error injection: `ctx.Add(field, message)`
- [x] Externally-added messages persist until explicitly cleared or field value changes
- [x] Unit tests: server-side errors injected and displayed
- [x] Unit tests: external errors cleared on field value change

#### 1A.8 Reconciler Integration
- [x] Wire validation context into Reactor reconciler lifecycle
- [x] Ensure validation runs after state updates, before rendering
- [x] Ensure validation messages are available to visualizers in the same render pass
- [x] Integration test: full component with producers → context → visualizer pipeline

---

### 1B. Control Error Styling (1–2 weeks)

#### 1B.1 Automatic Error Styling
- [x] In reconciler update path: when field has errors, set `BorderBrush` + `BorderThickness` on the WinUI control
- [x] Use WinUI theme resources:
  - [x] Error: `SystemFillColorCriticalBrush` (red)
  - [x] Warning: `SystemFillColorCautionBrush` (yellow/orange)
  - [x] Info: `SystemFillColorSolidNeutralBrush` (blue/gray) or no border change
- [x] Store original `BorderBrush`/`BorderThickness` values before overriding
- [x] On validation clear: `ClearValue(Control.BorderBrushProperty)` to restore theme default
- [x] Verify light/dark mode switching applies correct colors
- [x] Unit tests: border changes on error, reverts on valid
- [x] Unit tests: severity maps to correct brush

#### 1B.2 Custom Error Styling Overrides
- [x] Implement `.OnError(Action<ElementBuilder> configure)` extension
- [x] Implement `.OnWarning(Action<ElementBuilder> configure)` extension
- [x] Custom styling takes precedence over default styling
- [x] Unit tests: custom styling applied when error present
- [x] Unit tests: custom styling reverted when error cleared

#### 1B.3 Styling Gated by Touched State
- [x] Error styling only shows for touched fields (default behavior)
- [x] After form submit attempt: all fields marked touched, all styling shows
- [x] Respect `showWhen` overrides (Always, WhenTouched, WhenDirty, AfterFirstSubmit)
- [x] Unit tests: untouched field with errors shows no styling
- [x] Unit tests: touched field with errors shows styling
- [x] Unit tests: submit marks all touched

---

### 1C. Validation Visualizers (2–3 weeks)

#### 1C.1 Error Bubbling Infrastructure
- [x] Implement error bubbling through element tree: errors flow upward until caught by a visualizer
- [x] Visualizer "catches" errors from its content subtree, removing them from further bubbling
- [x] Uncaught errors continue to bubble to parent visualizers
- [x] Guarantee: every error is displayed somewhere (top-level default catch-all or explicit visualizer)
- [x] Unit tests: error caught by nearest visualizer
- [x] Unit tests: uncaught errors bubble to parent
- [x] Unit tests: no error is silently lost

#### 1C.2 Inline Visualizer (`.ShowErrors()`)
- [x] Implement `.ShowErrors()` extension on elements with `.Validate()`
- [x] Renders error text directly below the control
- [x] Error text uses severity-appropriate color (red for error, yellow for warning)
- [x] Animated show/hide transition
- [x] Unit tests: inline errors appear below control
- [x] Unit tests: multiple errors shown as list

#### 1C.3 Summary Visualizer
- [x] Implement `ValidationVisualizer(VisualizerStyle.Summary, content: ...)` element
- [x] Renders a bullet list of all errors from the content subtree
- [x] Placed above or below the section (configurable)
- [x] Shows field name + error text per message
- [x] Unit tests: summary collects errors from all child controls
- [x] Unit tests: summary updates when errors change

#### 1C.4 InfoBar Visualizer
- [x] Implement `ValidationVisualizer(VisualizerStyle.InfoBar, content: ...)` element
- [x] Renders a WinUI InfoBar with severity-appropriate color
- [x] Aggregated message: "Please fix N errors before submitting" (or individual messages)
- [x] InfoBar severity matches highest severity in collected errors
- [x] Closable (but reappears on next validation cycle if errors persist)
- [x] Unit tests: InfoBar shows/hides based on errors
- [x] Unit tests: InfoBar severity matches worst error

#### 1C.5 Custom Visualizer
- [x] Implement `ValidationVisualizer(render: Func<IReadOnlyList<ValidationMessage>, Element>, content: ...)` 
- [x] Custom render function receives all caught messages
- [x] Developer controls full layout
- [x] Unit tests: custom render receives correct messages

#### 1C.6 Visualizer Options
- [x] `severity` filter: only catch errors of specified severity (let others bubble)
- [x] `showWhen`: `Always`, `WhenTouched`, `AfterFirstSubmit`, `Never`
- [x] `title`: optional heading text for the visualizer
- [x] Unit tests: severity filter lets lower-severity messages bubble
- [x] Unit tests: showWhen gating works correctly

#### 1C.7 Hierarchical Composition
- [x] Test nested visualizers: section-level Summary + page-level InfoBar
- [x] Verify errors caught at section level don't appear at page level
- [x] Verify errors NOT caught at section level DO appear at page level
- [x] Integration test: full two-level visualizer hierarchy

---

### 1D. FormField Wrapper (1–2 weeks)

#### 1D.1 FormField Element
- [x] Create `Reactor/Validation/FormField.cs` (Reactor element)
- [x] Accept parameters: `label`, `required`, `description`, `content`
- [x] Layout: Label row → content control → description/error row
- [x] Show `*` indicator when `required: true`
- [x] Unit tests: renders label, content, description correctly

#### 1D.2 Auto-Wired Inline Visualizer
- [x] FormField automatically acts as inline visualizer for its child control
- [x] When child has errors: replace `description` text with error message(s)
- [x] Animated transition between description and error text (fade)
- [x] When child is valid: show description text
- [x] Unit tests: description swaps to error on invalid
- [x] Unit tests: error swaps back to description on valid

#### 1D.3 Accessibility
- [x] Set `AutomationProperties.Name` on the child control from the label text
- [x] Set `AutomationProperties.IsRequiredForForm` when `required: true`
- [x] Error text announced to screen readers (LiveRegion or equivalent)
- [x] Unit tests: automation properties set correctly

#### 1D.4 FormField WinUI Control
- [x] Ship FormField as a standalone WinUI `ContentControl` (usable from XAML)
- [x] DependencyProperties: `Header`, `IsRequired`, `Description`, `ErrorMessage`
- [x] Works with any child control (TextBox, ComboBox, NumberBox, CheckBox, etc.)
- [x] Unit tests: XAML-usable control renders correctly

---

### 1E. Dirty / Touched State Tracking (2–3 weeks)

#### 1E.1 Touched Tracking
- [x] Extend `ValidationContext` with touched state per field
- [x] Track via `GotFocus` / `LostFocus` hooks on controls with `.Validate()`
- [x] `ctx.IsTouched(string field)` query method
- [x] `ctx.MarkAllTouched()` — called on submit attempt
- [x] Unit tests: field starts untouched
- [x] Unit tests: focus + blur marks field as touched
- [x] Unit tests: `MarkAllTouched()` touches every registered field

#### 1E.2 Dirty Tracking
- [x] Extend `ValidationContext` with initial value storage per field
- [x] Store initial values at context creation / field registration
- [x] `ctx.IsDirty(string field)` — true when current value differs from initial
- [x] `ctx.IsDirty()` — true when any field is dirty
- [x] Unit tests: field starts not dirty
- [x] Unit tests: changing value makes field dirty
- [x] Unit tests: reverting to initial value makes field not dirty

#### 1E.3 Reset
- [x] `ctx.Reset(string field)` — restore field to initial value, clear touched state
- [x] `ctx.Reset()` — restore all fields to initial values, clear all touched states
- [x] Reset triggers controlled-input setters to update UI
- [x] Clear validation messages for reset fields
- [x] Unit tests: reset restores initial value
- [x] Unit tests: reset clears touched state
- [x] Unit tests: reset clears validation messages

#### 1E.4 Gating Behavior Integration
- [x] Default: inline error styling + text only appear for **touched** fields
- [x] Submit attempt → `MarkAllTouched()` → all errors visible
- [x] Summary/InfoBar visualizers show errors regardless of touched state
- [x] Respect `showWhen` overrides on individual visualizers
- [x] Integration test: full touched-gating scenario (type → blur → see error)
- [x] Integration test: submit shows all errors at once

---

## Phase 2: Controls & Input Features (2A–2D)

### 2A. MaskedTextBox (3–4 weeks)

#### 2A.1 Mask Engine
- [x] Create `Reactor/Controls/MaskedTextBox/MaskEngine.cs`
- [x] Implement mask token parsing:
  - [x] `0` = required digit
  - [x] `9` = optional digit
  - [x] `A` = required letter
  - [x] `a` = optional letter
  - [x] `*` = required alphanumeric
  - [x] All other characters = literal (auto-insert)
- [x] Implement `Apply(string input) → string formatted` core logic
- [x] Implement `GetRawValue(string formatted) → string raw` (strip literals)
- [x] Implement `IsComplete(string formatted) → bool` (all required slots filled)
- [x] Unit tests: each token type with valid/invalid chars
- [x] Unit tests: literal auto-insertion
- [x] Unit tests: raw value extraction
- [x] Unit tests: completion detection

#### 2A.2 MaskedTextBox WinUI Control
- [x] Create `Reactor/Controls/MaskedTextBox/MaskedTextBox.cs` inheriting from `TextBox`
- [x] DependencyProperties: `Mask`, `Placeholder` (char, default `_`), `RawValue` (read-only)
- [x] Hook `BeforeTextChanging` to intercept and enforce mask
- [x] Handle single character insertion at cursor position
- [x] Handle character deletion (backspace, delete) with cursor repositioning
- [x] Handle selection replacement
- [x] Handle clipboard paste (validate pasted content against mask)
- [x] Cursor management: auto-skip literal positions on navigation
- [x] Unit tests: typing characters into masked field
- [x] Unit tests: deleting characters (backspace and delete key)
- [x] Unit tests: paste handling (valid and invalid content)
- [x] Unit tests: cursor positioning after operations

#### 2A.3 MaskedTextBox Reactor DSL
- [x] Create `MaskedTextField()` Reactor element function
- [x] Parameters: `value`, `onChanged`, `mask`, `header`, `placeholder`
- [x] Wire to WinUI MaskedTextBox control via reconciler
- [x] Act as validation producer: incomplete mask emits Warning
- [x] Integrate with `.Validate()` extension
- [x] Unit tests: Reactor element creates and updates MaskedTextBox
- [x] Unit tests: incomplete mask produces warning message

#### 2A.4 Common Mask Presets
- [x] `MaskPreset.PhoneUS` → `(000) 000-0000`
- [x] `MaskPreset.SSN` → `000-00-0000`
- [x] `MaskPreset.ZipCode` → `00000`
- [x] `MaskPreset.ZipCodePlus4` → `00000-0000`
- [x] `MaskPreset.CreditCard` → `0000 0000 0000 0000`
- [x] `MaskPreset.Date` → `00/00/0000`
- [x] `MaskPreset.Time` → `00:00`
- [x] `MaskPreset.IPv4` → `099.099.099.099`
- [x] Unit tests: each preset produces correct formatting

---

### 2B. InputFormatters (2–3 weeks)

#### 2B.1 Formatter Infrastructure
- [x] Create `Reactor/Controls/Formatting/InputFormatter.cs`
- [x] Define formatter signature: `(string input, int cursorPos) → (string output, int newCursorPos)`
- [x] Implement formatter pipeline: chain multiple formatters in sequence
- [x] Implement bidirectional support: display value (formatted) vs. model value (raw)
- [x] Unit tests: single formatter application
- [x] Unit tests: pipeline of multiple formatters
- [x] Unit tests: cursor position maintained correctly after formatting

#### 2B.2 Built-in Formatters
- [x] `InputFormatter.PhoneUS` — `(555) 123-4567`
- [x] `InputFormatter.PhoneIntl` — `+1 555-123-4567` (configurable country code)
- [x] `InputFormatter.Currency(string symbol)` — `$1,234.56` (thousands separator + decimal limit)
- [x] `InputFormatter.UpperCase` — force uppercase
- [x] `InputFormatter.LowerCase` — force lowercase
- [x] `InputFormatter.TitleCase` — title case transform
- [x] `InputFormatter.TrimWhitespace` — strip leading/trailing spaces
- [x] `InputFormatter.MaxLength(int n)` — truncate at n characters
- [x] `InputFormatter.AllowOnly(string regex)` — character whitelist
- [x] `InputFormatter.DenyOnly(string regex)` — character blacklist
- [x] `InputFormatter.Custom(Func<string, string> format, Func<string, string> parse)` — developer-defined
- [x] Unit tests per formatter: input → expected output, cursor position

#### 2B.3 `.Format()` Extension
- [x] Implement `.Format(params InputFormatter[] formatters)` extension on TextField builder
- [x] Hook into `TextBox.BeforeTextChanging` to apply formatter pipeline
- [x] Ensure controlled-input model works (formatted display value, raw model value)
- [x] Unit tests: `.Format()` on TextField applies formatting on input
- [x] Unit tests: model value is raw, display value is formatted

#### 2B.4 WinUI Attached Behavior
- [x] Ship formatters as WinUI attached behaviors (usable from XAML)
- [x] `Formatting.Formatter` attached property accepts formatter instances
- [x] Works on any TextBox-derived control
- [x] Unit tests: attached behavior applies formatter in XAML scenario

---

### 2C. Async AutoSuggest with Templates (3–4 weeks)

#### 2C.1 AutoSuggest\<T\> Core
- [x] Create `Reactor/Controls/AutoSuggest.cs` (generic Reactor element)
- [x] Parameters: `selected`, `onSelected`, `search`, `displayText`, `placeholder`
- [x] Wrap WinUI `AutoSuggestBox`
- [x] Type-safe: `T` for selected item, search results are `IReadOnlyList<T>`
- [x] Unit tests: basic selection flow

#### 2C.2 Async Search with Debounce
- [x] `search: Func<string, CancellationToken, Task<IReadOnlyList<T>>>` parameter
- [x] Configurable `debounceMs` (default 300ms)
- [x] Cancel in-flight search when new input arrives
- [x] Cancel search on component unmount
- [x] Unit tests: debounce delays search execution
- [x] Unit tests: rapid typing cancels previous searches
- [x] Unit tests: unmount cancels in-flight search

#### 2C.3 Loading / Empty / Error States
- [x] Show loading indicator while async search is in progress
- [x] Show "No results" message when search returns empty
- [x] Show error state when search throws (configurable error message)
- [x] States rendered as special items in suggestion list
- [x] Unit tests: loading state shows during search
- [x] Unit tests: empty state shows on no results
- [x] Unit tests: error state shows on exception

#### 2C.4 Custom Item Templates
- [x] `template: Func<T, Element>` parameter for custom suggestion rendering
- [x] Wire to `AutoSuggestBox.ItemTemplate` (DataTemplate with Reactor rendering)
- [x] `displayText: Func<T, string>` for selected item text
- [x] Unit tests: custom template renders for each suggestion item

#### 2C.5 Validation Integration
- [x] `.Validate()` works on `AutoSuggest<T>` (e.g., `Required("Please select a user")`)
- [x] Validation fires on selection change
- [x] Unit tests: required validation on empty selection

---

### 2D. Focus Management — UseFocus (2–3 weeks)

#### 2D.1 UseFocus Hook
- [x] Create `Reactor/Hooks/UseFocus.cs`
- [x] Implement `UseFocus()` hook returning a `FocusManager` instance
- [x] `FocusManager` tracks registered controls by field name
- [x] Unit tests: hook creates FocusManager

#### 2D.2 `.Focus()` Extension
- [x] Implement `.Focus(FocusManager fm, string fieldName, bool autoFocus = false)` extension
- [x] Register control with FocusManager by field name
- [x] `autoFocus: true` → focus control on initial render
- [x] Unit tests: autoFocus fires on mount
- [x] Unit tests: control registered with correct field name

#### 2D.3 Programmatic Focus Control
- [x] `fm.FocusField(string fieldName)` — focus a specific field
- [x] `fm.FocusFirst(IReadOnlyList<string> fieldNames)` — focus first field in list
- [x] `fm.FocusNext()` — focus next registered field in order
- [x] `fm.FocusPrevious()` — focus previous registered field in order
- [x] Uses `Control.Focus(FocusState.Programmatic)` (existing WinUI API)
- [x] Unit tests: programmatic focus moves to correct control
- [x] Unit tests: FocusFirst with validation context's InvalidFields

#### 2D.4 Enter-to-Advance
- [x] Hook `KeyDown` on registered controls
- [x] Enter key → advance focus to next registered field
- [x] Enter on last field → trigger submit (configurable)
- [x] Shift+Enter → focus previous field (in multi-line contexts, respect newline)
- [x] Unit tests: Enter advances focus
- [x] Unit tests: Enter on last field triggers submit callback

#### 2D.5 Focus + Touched Integration
- [x] FocusManager feeds into touched state tracking (1E)
- [x] `GotFocus` / `LostFocus` events registered once (shared between focus and touched tracking)
- [x] Unit tests: focus/blur through FocusManager marks field as touched

---

## Phase 3: Higher-Level Patterns (3A–3D) — Future

### 3A. Multi-Step Form Wizard (3–4 weeks)

#### 3A.1 UseWizard Hook
- [ ] Create `Reactor/Hooks/UseWizard.cs`
- [ ] `UseWizard(params Step[] steps)` returns a `WizardState` instance
- [ ] `Step(string title, Func<Element> render)` definition
- [ ] State: `CurrentStepIndex`, `CanGoBack`, `CanGoNext`, `IsLastStep`, `IsFirstStep`
- [ ] Unit tests: initial state, step count, navigation properties

#### 3A.2 Navigation
- [ ] `wizard.GoNext()` — advance to next step (if current step is valid)
- [ ] `wizard.GoBack()` — return to previous step
- [ ] `wizard.GoTo(int stepIndex)` — jump to specific step (only if all prior steps valid)
- [ ] Navigation guards: `GoNext` triggers validation on current step, blocks if invalid
- [ ] On blocked navigation: mark all current step fields as touched
- [ ] Unit tests: navigation between steps
- [ ] Unit tests: validation blocks forward navigation
- [ ] Unit tests: backward navigation always allowed

#### 3A.3 Progress Indicator
- [ ] `wizard.ProgressBar()` — renders step indicator (step names, current position, completed states)
- [ ] Step states: `NotStarted`, `InProgress`, `Completed`, `Error`
- [ ] Clickable step names (jump to step, validation-gated)
- [ ] Unit tests: progress bar reflects current step
- [ ] Unit tests: completed steps shown correctly

#### 3A.4 Validation Integration
- [ ] Each step's content participates in the validation tree
- [ ] `wizard.IsCurrentStepValid` — current step has no errors
- [ ] `wizard.IsValid` — all steps valid (for final submit)
- [ ] Per-step validation context scoping
- [ ] Integration test: multi-step form with per-step validation

---

### 3B. FieldArray — Dynamic Repeated Sections (3–4 weeks)

#### 3B.1 UseFieldArray Hook
- [ ] Create `Reactor/Hooks/UseFieldArray.cs`
- [ ] `UseFieldArray<T>()` returns `FieldArrayState<T>`
- [ ] `Fields` property: `IReadOnlyList<T>` of current items
- [ ] Unit tests: initial empty array, initial with items

#### 3B.2 Array Operations
- [ ] `Append(T item)` — add item at end
- [ ] `Prepend(T item)` — add item at beginning
- [ ] `Insert(int index, T item)` — add item at position
- [ ] `Remove(int index)` — remove item at position
- [ ] `Update(int index, Func<T, T> updater)` — update item in place
- [ ] `Move(int from, int to)` — reorder items
- [ ] `Clear()` — remove all items
- [ ] Unit tests per operation

#### 3B.3 Validation Integration
- [ ] Each array item's controls participate in the validation tree
- [ ] Per-item error scoping (errors tied to specific item index)
- [ ] Adding/removing items updates validation state
- [ ] Integration test: field array with per-item validation

---

### 3C. AutoForm — Schema-Driven Generation (4–6 weeks)

#### 3C.1 Type Inspection
- [ ] Create `Reactor/Validation/AutoForm/TypeInspector.cs`
- [ ] Inspect C# record type via reflection
- [ ] Read `DataAnnotations` attributes: `[Required]`, `[EmailAddress]`, `[StringLength]`, `[Range]`, `[AllowedValues]`
- [ ] Read `[Display]` attribute: `Name`, `Prompt`, `Description`, `Order`, `GroupName`
- [ ] Map property types to control types:
  - [ ] `string` → TextField
  - [ ] `string` + `[EmailAddress]` → TextField with Email validation
  - [ ] `int` / `double` → NumberBox
  - [ ] `bool` → CheckBox
  - [ ] `string` + `[AllowedValues]` → ComboBox
  - [ ] `DateTime` → DatePicker
- [ ] Unit tests: type inspection reads annotations correctly
- [ ] Unit tests: type-to-control mapping for each supported type

#### 3C.2 AutoForm Element
- [ ] Create `Reactor/Validation/AutoForm/AutoForm.cs` (Reactor element)
- [ ] `AutoForm<T>(T value, Action<T> onChange, Action onSubmit, VisualizerStyle visualizer)`
- [ ] Auto-generate form layout from type inspection
- [ ] Wrap each field in `FormField` with label from `[Display(Name)]` or property name
- [ ] Wire validation from DataAnnotations → `.Validate()` rules
- [ ] Add submit button gated on `ctx.IsValid()`
- [ ] Unit tests: auto-generated form matches expected layout
- [ ] Integration test: full form from record type with validation

#### 3C.3 Customization
- [ ] Override control for specific fields: `AutoForm(...).Override("Role", el => ComboBox(...))`
- [ ] Custom field ordering: `[Display(Order = N)]`
- [ ] Field grouping: `[Display(GroupName = "Section")]`
- [ ] Exclude fields: `[AutoFormIgnore]` custom attribute
- [ ] Unit tests: customizations applied correctly

---

### 3D. Inline Editable DataGrid (8–12 weeks — separate workstream)

#### 3D.1 DataGrid Core
- [ ] Create `Reactor/Controls/DataGrid/DataGrid.cs` (WinUI control)
- [ ] Column definition API: `Column(string header, Func<T, object> binding)`
- [ ] Read-only rendering: display tabular data
- [ ] Row virtualization for performance (large datasets)
- [ ] Unit tests: basic rendering, column definition, data binding
- [ ] Performance test: 10,000+ rows render within acceptable time

#### 3D.2 Sorting & Filtering
- [ ] `sortable: true` — click column header to sort
- [ ] Multi-column sort (shift+click)
- [ ] Column filter UI (text filter, value selection)
- [ ] Programmatic sort/filter API
- [ ] Unit tests: sort ascending/descending
- [ ] Unit tests: multi-column sort
- [ ] Unit tests: filter reduces visible rows

#### 3D.3 Selection
- [ ] `selectionMode`: `None`, `Single`, `Multiple`
- [ ] `onSelectionChanged` callback
- [ ] Ctrl+click for multi-select, Shift+click for range select
- [ ] `SelectAll` / `ClearSelection` API
- [ ] Unit tests: selection modes, callbacks

#### 3D.4 Inline Editing
- [ ] `editable: true` on column definition
- [ ] Double-click or Enter to enter edit mode on a cell
- [ ] Escape to cancel edit, Tab/Enter to confirm
- [ ] `onRowChanged` callback with index and updated item
- [ ] Edit mode renders appropriate control per column type (TextField, NumberBox, CheckBox, ComboBox)
- [ ] Unit tests: enter/exit edit mode
- [ ] Unit tests: edit commits and cancels

#### 3D.5 Cell-Level Validation
- [ ] `.Validate()` on column definitions
- [ ] Each editable cell acts as a validation producer
- [ ] Grid row acts as inline visualizer for its cells
- [ ] Invalid cells show error styling (border color)
- [ ] Row-level error summary (tooltip or inline)
- [ ] Unit tests: cell validation on edit
- [ ] Integration test: edit + validate + save flow

#### 3D.6 DataGrid Reactor DSL
- [ ] Wrap WinUI DataGrid control as Reactor element
- [ ] Declarative column definitions with lambdas
- [ ] Integrate with Reactor state management (controlled selection, editing)
- [ ] Unit tests: Reactor DSL creates and updates DataGrid

---

## Cross-Cutting Concerns

### CC.1 Accessibility
- [ ] All validation errors announced to screen readers via LiveRegion
- [ ] `aria-invalid` equivalent set on controls with errors
- [ ] Focus management respects tab order
- [ ] FormField sets appropriate automation properties
- [ ] Keyboard navigation through visualizer error lists
- [ ] Manual accessibility audit with Narrator / NVDA

### CC.2 Localization
- [ ] All built-in validator error messages are localizable via `UseIntl()`
- [ ] Severity labels localizable
- [ ] FormField required indicator (`*`) and label formatting localizable
- [ ] Visualizer default titles localizable
- [ ] Unit tests: localized messages appear when locale provider present
- [ ] Unit tests: English fallback when no locale provider

### CC.3 Documentation & Samples
- [ ] API reference documentation for all public types
- [ ] Sample: Basic form with inline validation
- [ ] Sample: Multi-section form with Summary + InfoBar visualizers
- [ ] Sample: Registration form (full example from proposal)
- [ ] Sample: MaskedTextBox with common patterns
- [ ] Sample: Async AutoSuggest with API integration
- [ ] Sample: Multi-step wizard form
- [ ] Sample: DataGrid with inline editing (Phase 3)

### CC.4 Performance
- [ ] Benchmark: validation overhead per render cycle (target: <1ms for 20 fields)
- [ ] Benchmark: error bubbling with deep nesting (10+ levels)
- [ ] Ensure validators don't cause unnecessary re-renders
- [ ] Async validator debounce prevents API flooding
- [ ] DataGrid virtual scrolling handles 10,000+ rows without lag
