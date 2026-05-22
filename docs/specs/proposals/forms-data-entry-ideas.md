# Forms & Data Entry — Validation Architecture and Feature Proposals

**Date:** April 2026
**Status:** Proposal
**Context:** Microsoft.UI.Reactor (Reactor) is graded C+ on Forms & Data Entry (overview scorecard). WPF is
A — the richest validation system of any framework. WinUI 3 is B — missing a
validation framework entirely. LOB forms are the primary use case for Windows
desktop apps.

---

## Current State

**What Reactor has today:**
- Controlled inputs: TextField, PasswordBox, NumberBox, ComboBox, RadioButtons,
  CheckBox, ToggleSwitch, Slider, RatingControl, ColorPicker, date/time pickers
- Validation: plain C# logic derived from state on every render
- Error display: `When()` + red Text — manual, ad-hoc, no consistent styling
- No form container, no touched/dirty tracking, no error aggregation

**What WinUI has today:**
- Header + Description properties on TextBox, PasswordBox, ComboBox, NumberBox
- NumberBox.ValidationMode (InvalidInputOverwritten / Disabled)
- No validation framework, no ErrorTemplate system, no error visual states

**What WPF has (the gold standard on Windows):**
- INotifyDataErrorInfo (async, multiple errors per field, ErrorsChanged event)
- ValidationRule on Bindings, BindingGroup for transactional edits
- ErrorTemplate (fully customizable XAML error visuals)

---

## Architecture: Producers, Control Styling, and Visualizers

The core insight is separating three concerns that every other framework
conflates:

| Concern | Responsibility | Who decides? |
|---------|---------------|-------------|
| **Producer** | Declares *what's wrong* | Validation rules, async checks, business logic |
| **Control Styling** | Makes the control *look* wrong | Framework default (red border), developer override |
| **Visualizer** | Shows the error *text* somewhere | Developer places visualizers at desired scope |

These are independent and composable:
- Control styling + inline visualizer → field goes red AND shows error below
- Control styling + NO local visualizer → field goes red, text appears in summary elsewhere
- No control styling + visualizer only → error text at top of form, controls look normal

### Error Bubbling Model

Errors flow **upward** through the element tree until a Visualizer catches
them. Every error is guaranteed to be displayed somewhere.

```
┌─ Window ──────────────────────────────────────────────┐
│  ValidationVisualizer (InfoBar)          ← catches    │
│  ┌─ Section A ─────────────────────────┐  uncaught    │
│  │  ValidationVisualizer (Summary)     │  errors      │
│  │  ┌────────────────────────────────┐ │              │
│  │  │ TextField → [Email error] ─────┼─┤→ caught     │
│  │  │ TextField → [Name error] ──────┼─┤→ caught     │
│  │  └────────────────────────────────┘ │              │
│  └─────────────────────────────────────┘              │
│  ┌─ Section B (no visualizer) ─────────┐              │
│  │  NumberBox → [Age error] ───────────┼──┤→ bubbles  │
│  │  CheckBox → [Terms error] ──────────┼──┤→ bubbles  │
│  └─────────────────────────────────────┘   → to top   │
└───────────────────────────────────────────────────────┘
```

### Severity Levels

Three tiers, matching WinUI's InfoBar severity:

| Severity | Blocks submit? | Example |
|----------|---------------|---------|
| **Error** | Yes | "Email is required" |
| **Warning** | No | "This username is taken in another org" |
| **Info** | No | "Password strength: weak" |

### Validation Message

```csharp
public record ValidationMessage(
    string Field,                        // which field produced this
    string Text,                         // human-readable message
    Severity Severity = Severity.Error,  // Error | Warning | Info
    string? Code = null                  // machine-readable (for i18n, dedup)
);
```

### WinUI Core Changes: Not Required

**The entire system is built on top of WinUI, like FlexPanel.** No changes to
WinUI control source code are needed. Specifics:

| Capability | Built-on-top approach | WinUI change that would be nice (not required) |
|---|---|---|
| **Control error styling** | Set `BorderBrush` / `BorderThickness` directly on control instances from reconciler (Reactor) or attached property callbacks (XAML). Store + restore originals. | Add `ValidationStates` visual state group to control templates for animated transitions. |
| **Error text below fields** | Our `FormField` wrapper adds error text as a sibling element below the control. | Add `Description` property to controls that lack it (CheckBox, ToggleSwitch, RadioButtons, Slider). |
| **Validation context** | Reactor's UseContext/UseRef infrastructure. For XAML: attached properties on a parent panel. | None — this is app-layer plumbing. |
| **Focus on first error** | `Control.Focus(FocusState.Programmatic)` — already public API. | None. |

**Why this matters:** We can ship the entire validation system without blocking
on the WinUI team. If WinUI later adds `ValidationStates` to templates, we
get smoother animations for free — but the feature is complete without it.

---

## Part 1: The Validation Core

### 1A. Validation Context and Producers

**What:** The plumbing that collects validation messages from any source and
routes them to visualizers. This is the foundation everything else builds on.

**Three types of producers:**

**Inline rules on controls** (most common):
```csharp
TextField(email, setEmail, header: "Email")
    .Validate(Required(), Email(), MaxLength(254))

NumberBox(age, setAge, header: "Age")
    .Validate(Range(18, 120))

CheckBox(terms, setTerms, label: "I accept the terms")
    .Validate(MustBeTrue("You must accept the terms"))
```

**Cross-field rules** (the "if checkbox Y then require field Z" case):
```csharp
// Placed anywhere in the tree — errors bubble to nearest visualizer
ValidationRule(
    () => !useCustomAddress || !string.IsNullOrEmpty(address),
    "Address is required when using custom shipping",
    field: "address"
)

ValidationRule(
    () => password == confirmPassword,
    "Passwords must match",
    field: "confirmPassword"
)
```

**Async / external producers** (server-side validation, API responses):
```csharp
var validation = UseValidationContext();

async Task Submit()
{
    var result = await _api.CreateAccount(email, username);
    if (result.Errors is { } errors)
    {
        validation.Add("email", "This email is already registered");
        validation.Add("username", "Username taken", Severity.Warning);
    }
}
```

**Built-in validators:**

| Validator | Applies to | Example |
|-----------|-----------|---------|
| `Required()` | Any | Field must have a non-default value |
| `MinLength(n)` / `MaxLength(n)` | string | Character count bounds |
| `Range(min, max)` | numeric | Value bounds |
| `Match(regex)` | string | Pattern match |
| `Email()` | string | Email format (built-in regex) |
| `Url()` | string | URL format |
| `Must(predicate, msg)` | Any | Custom lambda |
| `MustAsync(predicate, msg)` | Any | Async custom lambda |
| `MustBeTrue(msg)` | bool | Checkbox must be checked |
| `EqualTo(otherValue, msg)` | Any | Cross-field equality |

Custom validators are just lambdas — no interface to implement:
```csharp
.Validate(Must(v => !bannedWords.Contains(v), "Contains banned word"))
```

**Effort:** 3-4 weeks. Core context, message routing, built-in validators,
integration with Reactor reconciler.

**WinUI bonus:** Indirect. The validators are Reactor-side, but the attached
property bridge (see 1C) makes them usable from XAML.

---

### 1B. Control Error Styling

**What:** When a control has validation errors, it visually changes — red
border, warning border, etc. This happens automatically for any control
participating in validation, and can be customized.

**Default behavior (automatic):**

Any control with `.Validate()` gets styled automatically. The reconciler
reads the validation context on every update and applies styling:

```csharp
// Just adding .Validate() opts the control into error styling
TextField(email, setEmail).Validate(Required(), Email())
// → border turns red when invalid + touched, normal when valid
```

**Implementation (no WinUI changes):**

The reconciler sets properties directly on the WinUI control:

```csharp
// In Reconciler.Update — when field has errors:
textBox.BorderBrush = _errorBrush;      // SystemFillColorCriticalBrush
// or for warnings:
textBox.BorderBrush = _warningBrush;    // SystemFillColorCautionBrush

// When field is valid — restore:
textBox.ClearValue(TextBox.BorderBrushProperty);  // back to theme default
```

Uses WinUI theme resources so it works in light/dark mode automatically:
- Error: `SystemFillColorCriticalBrush` (red)
- Warning: `SystemFillColorCautionBrush` (yellow/orange)
- Info: `SystemFillColorSolidNeutralBrush` (blue/gray — or no border change)

**Custom styling (developer override):**

```csharp
// Override the default error appearance
TextField(email, setEmail)
    .Validate(Required(), Email())
    .OnError(el => el.BorderBrush("#ff0000").BorderThickness(2))
    .OnWarning(el => el.BorderBrush("#ffa500").BorderThickness(1))
```

**Querying validation state for arbitrary elements:**

The validation context is available to any element in the tree, not just
the control with the error. This lets you style labels, icons, sections:

```csharp
var ctx = UseValidationContext();

VStack(8,
    // Label turns red when field has errors
    Text("Email")
        .Foreground(ctx.HasError("email") ? "#d13438" : inherit)
        .Bold(ctx.HasError("email")),

    TextField(email, setEmail).Validate(Required(), Email()),

    // Icon shows severity
    When(ctx.HasMessages("email"), () =>
        FontIcon(ctx.HighestSeverity("email") == Severity.Error
            ? Symbol.ErrorBadge : Symbol.Warning)
    )
)
```

**Effort:** 1-2 weeks (ships with 1A as part of the validation core).

**WinUI bonus:** Works on any WinUI control today via direct property setting.
No template changes needed. If WinUI later adds `ValidationStates` to
templates, we switch from property-setting to `VisualStateManager.GoToState()`
for smoother animated transitions — a one-line change in the reconciler.

---

### 1C. Validation Visualizers

**What:** Components that catch validation messages at a chosen scope and
display them. The developer places visualizers wherever they want error UI.
Errors bubble up through the tree until caught.

**Four built-in visualizer styles:**

**Inline** — text below a single field:
```csharp
TextField(email, setEmail)
    .Validate(Required(), Email())
    .ShowErrors()                    // inline text below this control
```

**Summary** — bullet list for a section:
```csharp
ValidationVisualizer(VisualizerStyle.Summary,
    VStack(12,
        SubHeading("Account Info"),
        TextField(email, setEmail).Validate(Required(), Email()),
        TextField(username, setUsername).Validate(Required()),
    )
)
// Renders a bullet list of errors above/below the section
```

**InfoBar** — WinUI InfoBar with severity-appropriate color:
```csharp
ValidationVisualizer(VisualizerStyle.InfoBar,
    content: entireFormContent
)
// Renders a closable InfoBar: "Please fix 3 errors before submitting"
```

**Custom** — render errors however you want:
```csharp
ValidationVisualizer(
    render: messages => VStack(4,
        ForEach(messages, m => HStack(4,
            FontIcon(m.Severity == Severity.Error ? Symbol.Error : Symbol.Warning)
                .Foreground(m.Severity == Severity.Error ? "#d13438" : "#ffa500"),
            Text(m.Text).FontSize(12)
        ))
    ),
    content: formContent
)
```

**Visualizer options:**

```csharp
ValidationVisualizer(
    style: VisualizerStyle.Summary,
    severity: Severity.Error,              // only catch errors (let warnings bubble)
    showWhen: ShowWhen.AfterFirstSubmit,   // or: Always, WhenTouched, Never
    title: "Please fix the following:",
    content: ...
)
```

**Hierarchical composition:**

```csharp
// Top-level: catches everything uncaught
ValidationVisualizer(VisualizerStyle.InfoBar,
    content: VStack(16,

        // Section 1: local visualizer catches its own errors
        ValidationVisualizer(VisualizerStyle.Summary,
            content: VStack(12,
                SubHeading("Account"),
                TextField(email, setEmail).Validate(Required(), Email()),
                PasswordBox(pw, setPw).Validate(Required(), MinLength(8)),
            )
        ),

        // Section 2: no visualizer — errors bubble to top InfoBar
        VStack(12,
            SubHeading("Preferences"),
            NumberBox(age, setAge).Validate(Range(18, 120)),
            CheckBox(terms, setTerms).Validate(MustBeTrue("Required")),
        ),

        Button("Submit", HandleSubmit)
            .IsEnabled(UseValidationContext().IsValid())
    )
)
```

**Effort:** 2-3 weeks. InfoBar and Summary are composition of existing
elements. The bubbling/routing logic is the core work.

**WinUI bonus:** Yes. The visualizer components (especially InfoBar-based
summary) ship as standalone WinUI controls usable from XAML via attached
properties.

---

### 1D. FormField Wrapper

**What:** A layout component that wraps any input with consistent label,
required indicator, help text, and auto-wired error display. This is the
inline visualizer + label + description packaged as a single element.

```csharp
FormField("Email",
    required: true,
    description: "We'll never share your email",
    content: TextField(email, setEmail, placeholder: "you@example.com")
        .Validate(Required(), Email())
)
```

Renders as:
```
Email *
┌───────────────────────────┐
│ you@example.com           │  ← normal state
└───────────────────────────┘
We'll never share your email

Email *
┌───────────────────────────┐
│ bad-input                 │  ← error state (border from 1B)
└───────────────────────────┘
Invalid email format            ← error replaces description (from 1C)
```

**Key points:**
- Automatically acts as an inline visualizer for its child control
- Replaces `description` with error text when invalid (animated transition)
- Adds `*` required indicator
- Sets `AutomationProperties.Name` for accessibility
- Works with any control, not just TextBox

**Implementation (no WinUI changes):**
- Pure Reactor element: composes Text (label) + child + Text (description/error)
- For XAML: ships as a `FormField` ContentControl similar to `Expander`
- Description swap uses existing Reactor animation for fade transitions
- Does NOT use WinUI's built-in `Header`/`Description` properties (those are
  internal to each control's template). Instead, wraps externally — works with
  every control uniformly, including CheckBox and Slider which lack Description.

**Effort:** 1-2 weeks.

**WinUI bonus:** Yes. Ships as a standalone WinUI control.

---

### 1E. Dirty / Touched State Tracking

**What:** Integrated into the validation context. Tracks which fields have
been modified (dirty) and interacted with (touched). Gates error display
so users aren't yelled at before they've had a chance to type.

**How it works:**

Built into the validation context, not a separate hook:

```csharp
var ctx = UseValidationContext();

// Touched: user has focused + blurred the field (or form was submitted)
ctx.IsTouched("email")    // bool

// Dirty: value differs from initial
ctx.IsDirty("email")      // bool
ctx.IsDirty()             // any field dirty?

// Reset
ctx.Reset("email")        // restore field to initial, clear touched
ctx.Reset()               // restore all fields

// Unsaved changes check
When(ctx.IsDirty(), () =>
    InfoBar("You have unsaved changes", severity: InfoBarSeverity.Warning))
```

**Gating behavior:**
- By default, inline error styling and text only appear for **touched** fields
- Attempting to submit marks all fields as touched (shows all errors at once)
- Visualizers can override: `showWhen: ShowWhen.Always` or `ShowWhen.WhenDirty`
- Summary/InfoBar visualizers show errors regardless of touched state (they're
  form-level, not field-level)

**Implementation:**
- Touched: reconciler hooks into GotFocus/LostFocus on controls with `.Validate()`
- Dirty: validation context stores initial values at creation, compares on each render
- Reset: sets state back to initial values via the controlled-input setters

**Effort:** 2-3 weeks (integrated into 1A).

**WinUI bonus:** No — this is state management, not a control.

---

## Part 2: Controls and Input Features

### 2A. MaskedTextBox

**What:** A text input that enforces a format pattern — phone `(___) ___-____`,
SSN `___-__-____`, credit cards, dates, postal codes, IP addresses.

```csharp
// Reactor
MaskedTextField(phone, setPhone, mask: "(000) 000-0000",
    header: "Phone Number")
    .Validate(Required())

MaskedTextField(ssn, setSsn, mask: "000-00-0000",
    placeholder: '_')
```

**Mask tokens:** `0` = required digit, `9` = optional digit, `A` = required
letter, `a` = optional letter, `*` = required alphanumeric. All other
characters are literals that auto-insert.

**Implementation (no WinUI changes):**
- New control inheriting from `TextBox` (same pattern as NumberBox)
- Mask engine intercepts `BeforeTextChanging` to enforce format
- Handles insertion, deletion, selection, clipboard paste
- Raw value property returns unformatted text (digits only for phone)
- Ships as standalone WinUI control + Reactor DSL wrapper
- Acts as a validation **producer** — incomplete masks emit a Warning

**Market prevalence:** Very high. WinForms MaskedTextBox, DevExpress/Telerik
masked input, react-input-mask (1.2M weekly npm). 70-80% of LOB apps use at
least one masked field.

**Effort:** 3-4 weeks. Mask parsing is well-understood; cursor management
during insert/delete and clipboard paste are the tricky parts.

**WinUI bonus:** Yes — direct. Ships as a WinUI control usable from XAML.

---

### 2B. InputFormatters — Real-Time Value Transformation

**What:** Pluggable formatters that transform text as the user types.
Lighter-weight than MaskedTextBox, applicable to any TextField.

```csharp
TextField(phone, setPhone)
    .Format(InputFormatter.PhoneUS)          // (555) 123-4567

TextField(price, setPrice)
    .Format(InputFormatter.Currency("$"))    // $1,234.56

TextField(code, setCode)
    .Format(InputFormatter.UpperCase,        // forces uppercase
            InputFormatter.AllowOnly("[A-Z0-9]"),  // filter chars
            InputFormatter.MaxLength(6))     // truncate
```

**Built-in formatters:**

| Formatter | Effect |
|-----------|--------|
| `PhoneUS` / `PhoneIntl` | Auto-format as phone number |
| `Currency(symbol)` | Prefix + thousands separator + decimal limit |
| `UpperCase` / `LowerCase` / `TitleCase` | Case transform |
| `TrimWhitespace` | Strip leading/trailing spaces |
| `MaxLength(n)` | Truncate at n characters |
| `AllowOnly(regex)` / `DenyOnly(regex)` | Character filter |
| `Custom(format, parse)` | Developer-defined transform |

**Implementation (no WinUI changes):**
- Formatters hook into `TextBox.BeforeTextChanging` (WinUI public event)
- Each formatter is a function: `(string input, int cursorPos) → (string output, int newCursorPos)`
- Composable: chain multiple formatters in a pipeline
- Bidirectional: display value (formatted) vs. model value (raw)
- Ships as WinUI attached behavior + Reactor `.Format()` extension

**Effort:** 2-3 weeks. Cursor position management after formatting is the
hardest part.

**WinUI bonus:** Yes. Ships as WinUI attached behaviors usable from XAML.

---

### 2C. Async AutoSuggest with Templates

**What:** Enhanced AutoSuggestBox that supports async data sources, debouncing,
loading/empty/error states, custom item templates, and type safety.

```csharp
AutoSuggest<User>(
    selected: selectedUser,
    onSelected: setSelectedUser,
    search: async (query, cancel) => await _api.SearchUsers(query, cancel),
    template: user => HStack(8,
        PersonPicture(user.AvatarUrl).Size(24),
        VStack(2,
            Text(user.DisplayName).Bold(),
            Text(user.Email).FontSize(12).Opacity(0.6)
        )
    ),
    displayText: user => user.DisplayName,
    placeholder: "Search users...",
    debounceMs: 300
)
.Validate(Required("Please select a user"))
```

**Implementation (no WinUI changes):**
- Wraps existing WinUI `AutoSuggestBox` — no new control needed
- Async search with `CancellationToken` and configurable debounce
- Custom item templates via `AutoSuggestBox.ItemTemplate` (already supported)
- Loading/empty/error states shown as special items in the suggestion list
- Generic `AutoSuggest<T>` provides type safety

**Effort:** 3-4 weeks.

**WinUI bonus:** Partial. The async data pattern could be extracted as a
reusable helper for WinUI's AutoSuggestBox.

---

### 2D. Focus Management (UseFocus)

**What:** Programmatic focus control for forms — auto-focus first field,
enter-to-advance, focus first invalid field on submit failure.

```csharp
var focus = UseFocus();

TextField(name, setName).Focus(focus, "name", autoFocus: true)
TextField(email, setEmail).Focus(focus, "email")
NumberBox(age, setAge).Focus(focus, "age")
Button("Submit", () => {
    var ctx = UseValidationContext();
    if (!ctx.IsValid())
        focus.FocusFirst(ctx.InvalidFields);  // jump to first error
    else
        Submit();
})
```

**Implementation (no WinUI changes):**
- Uses `Control.Focus(FocusState.Programmatic)` — existing public API
- Enter-to-advance: hooks `KeyDown` on registered controls
- Focus tracking: hooks `GotFocus` / `LostFocus` (feeds into touched state)

**Effort:** 2-3 weeks.

**WinUI bonus:** Partial. Could ship as attached behaviors for XAML.

---

## Part 3: Higher-Level Patterns (Future)

### 3A. Multi-Step Form Wizard

**What:** Hook + component for multi-step forms with progress tracking, per-step
validation, and navigation guards.

```csharp
var wizard = UseWizard(
    Step("Personal Info", () => PersonalInfoStep()),
    Step("Address",       () => AddressStep()),
    Step("Payment",       () => PaymentStep()),
    Step("Review",        () => ReviewStep())
);

return VStack(16,
    wizard.ProgressBar(),
    wizard.CurrentStep(),
    HStack(12,
        When(wizard.CanGoBack, () => Button("Back", wizard.GoBack)),
        When(!wizard.IsLastStep, () =>
            Button("Next", wizard.GoNext).IsEnabled(wizard.IsCurrentStepValid)),
        When(wizard.IsLastStep, () =>
            Button("Submit", HandleSubmit).IsEnabled(wizard.IsValid))
    )
);
```

**Integration with validation architecture:** Each step's content can contain
its own producers (`.Validate()` rules) and visualizers. `wizard.GoNext`
triggers validation for the current step — if invalid, marks all fields
touched and prevents advancement.

**Effort:** 3-4 weeks.

**WinUI bonus:** Partial. The progress bar / step indicator could ship as a
standalone WinUI control.

---

### 3B. FieldArray — Dynamic Repeated Sections

**What:** Manage dynamic lists of form items — invoice line items, multiple
addresses, phone numbers. Add, remove, reorder, validate each item.

```csharp
var items = UseFieldArray<LineItem>();

return VStack(12,
    ForEach(items.Fields, (item, index) =>
        ValidationVisualizer(VisualizerStyle.Inline,
            content: HStack(12,
                TextField(item.Description,
                    v => items.Update(index, i => i with { Description = v }))
                    .Validate(Required()),
                NumberBox(item.Quantity,
                    v => items.Update(index, i => i with { Quantity = v }))
                    .Validate(Range(1, 9999)),
                Button("Remove", () => items.Remove(index))
            )
        )
    ),
    Button("Add Item", () => items.Append(new LineItem()))
);
```

Each array item participates in the validation tree — errors from item fields
bubble like any other.

**Effort:** 3-4 weeks.

**WinUI bonus:** No — this is Reactor state management.

---

### 3C. AutoForm — Schema-Driven Form Generation

**What:** Generate a complete form from a C# record + DataAnnotations.

```csharp
public record ContactForm
{
    [Required, EmailAddress]
    [Display(Name = "Email", Prompt = "you@example.com")]
    public string Email { get; init; } = "";

    [Required, StringLength(50)]
    public string Name { get; init; } = "";

    [Range(18, 120)]
    public int? Age { get; init; }

    [Required]
    [AllowedValues("Developer", "Designer", "Manager")]
    public string Role { get; init; } = "";
}

var (contact, setContact) = UseState(new ContactForm());
return AutoForm(contact, setContact,
    onSubmit: HandleSubmit,
    visualizer: VisualizerStyle.Summary
);
```

AutoForm auto-wires:
- Control selection (type → control mapping)
- Labels and placeholders (from `[Display]`)
- Producers (from DataAnnotations → `.Validate()` rules)
- FormField wrappers (label + error + description)
- A visualizer at the form level
- Submit button gated on `ctx.IsValid()`

**Effort:** 4-6 weeks.

**WinUI bonus:** Indirect. Could be ported to a XAML source generator.

---

### 3D. Inline Editable DataGrid

**What:** Data grid with inline cell editing, row validation, sorting,
filtering, and virtualization. The #1 most-requested LOB control.

```csharp
DataGrid(products,
    columns: [
        Column("Name", p => p.Name, editable: true)
            .Validate(Required()),
        Column("Price", p => p.Price, editable: true, format: "C2")
            .Validate(Range(0.01, 99999)),
        Column("Stock", p => p.Stock, editable: true),
    ],
    onRowChanged: (index, product) =>
        setProducts(ps => ps.SetItem(index, product)),
    sortable: true,
    selectionMode: SelectionMode.Multiple
)
```

Cell-level validation integrates with the producer/visualizer model — each
editable cell is a producer, and the grid row acts as an inline visualizer.

**Effort:** 8-12 weeks (separate workstream).

**WinUI bonus:** Yes, significant. Ships as a standalone WinUI control.

---

## Full Example: Putting It All Together

```csharp
class RegistrationForm : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (password, setPw) = UseState("");
        var (confirmPw, setConfirmPw) = UseState("");
        var (age, setAge) = UseState(0.0);
        var (role, setRole) = UseState(0);
        var (terms, setTerms) = UseState(false);
        var (phone, setPhone) = UseState("");

        var ctx = UseValidationContext();
        var focus = UseFocus();

        return ValidationVisualizer(VisualizerStyle.InfoBar,  // top-level catch-all
            content: VStack(16,

                Heading("Create Account"),

                // Account section — errors shown inline per-field
                VStack(12,
                    FormField("Email", required: true,
                        description: "We'll never share your email",
                        content: TextField(email, setEmail)
                            .Validate(Required(), Email())
                            .Focus(focus, "email", autoFocus: true)),

                    FormField("Phone",
                        content: MaskedTextField(phone, setPhone,
                            mask: "(000) 000-0000")),

                    FormField("Password", required: true,
                        content: PasswordBox(password, setPw)
                            .Validate(Required(), MinLength(8))
                            .Focus(focus, "password")),

                    FormField("Confirm Password", required: true,
                        content: PasswordBox(confirmPw, setConfirmPw)
                            .Validate(Required(),
                                EqualTo(password, "Passwords must match"))
                            .Focus(focus, "confirmPw")),
                ),

                // Preferences section — errors shown as summary
                ValidationVisualizer(VisualizerStyle.Summary,
                    content: VStack(12,
                        SubHeading("Preferences"),
                        FormField("Age",
                            content: NumberBox(age, setAge)
                                .Validate(Range(18, 120))),
                        FormField("Role",
                            content: ComboBox(
                                ["Developer", "Designer", "Manager"],
                                role, setRole)),
                    )
                ),

                // Terms — no local visualizer, bubbles to top InfoBar
                CheckBox(terms, setTerms, label: "I accept the terms")
                    .Validate(MustBeTrue("You must accept the terms")),

                // Submit
                HStack(12,
                    Button("Register", async () => {
                        if (!ctx.IsValid())
                        {
                            focus.FocusFirst(ctx.InvalidFields);
                            return;
                        }
                        var result = await _api.Register(email, password);
                        if (result.Errors is { } errors)
                            foreach (var e in errors)
                                ctx.Add(e.Field, e.Message);
                    }).IsEnabled(ctx.IsValid()),

                    When(ctx.IsDirty(), () =>
                        Button("Reset", () => ctx.Reset())
                            .Style(ButtonStyle.Subtle)),
                )
            )
        );
    }
}
```

---

## Summary Matrix

| # | Feature | Prevalence | Effort | WinUI Bonus | Requires WinUI Changes? |
|---|---------|-----------|--------|-------------|------------------------|
| **1A** | Validation Context + Producers | Universal | 3-4w | Indirect | No |
| **1B** | Control Error Styling | Universal | 1-2w | Works on all controls | No (nice-to-have: ValidationStates) |
| **1C** | Validation Visualizers | Universal | 2-3w | Yes (InfoBar summary) | No |
| **1D** | FormField Wrapper | Very High | 1-2w | Yes (ships as control) | No |
| **1E** | Dirty/Touched Tracking | Very High | 2-3w | No | No |
| **2A** | MaskedTextBox | Very High (70-80%) | 3-4w | Yes (ships as control) | No |
| **2B** | InputFormatters | High | 2-3w | Yes (attached behaviors) | No |
| **2C** | Async AutoSuggest | Very High | 3-4w | Partial | No |
| **2D** | Focus Management | High (a11y) | 2-3w | Partial | No |
| **3A** | Multi-Step Wizard | High (40-50%) | 3-4w | Partial | No |
| **3B** | FieldArray | High (30%) | 3-4w | No | No |
| **3C** | AutoForm | Moderate-High | 4-6w | Indirect | No |
| **3D** | DataGrid | Extremely High | 8-12w | Yes (ships as control) | No |

**Every feature ships without WinUI core changes.**

### Nice-to-Have WinUI Changes (Not Blocking)

If the WinUI team is interested, these would improve the experience:

1. **`ValidationStates` visual state group** on TextBox, PasswordBox, ComboBox,
   NumberBox templates — enables animated border transitions instead of direct
   property setting. ~1 day of template work per control.

2. **`Description` property** on CheckBox, ToggleSwitch, RadioButtons, Slider —
   these controls lack it today, so FormField wraps externally instead of
   using the native property. Not blocking (FormField works either way).

3. **`InputScope` improvements** — better soft keyboard hints for masked/
   formatted inputs on touch devices.

---

## Recommended Sequencing

**Phase 1 — Validation Core (5-8 weeks): NOW**
Items 1A through 1E. After this, Reactor has a complete validation system with
producers, control styling, visualizers, FormField, and dirty/touched. This
alone moves the Forms grade from **C+ to B+**.

**Phase 2 — Controls (5-8 weeks): NOW**
Items 2A through 2D. New controls (MaskedTextBox, InputFormatters) that
raise the bar for WinUI + Reactor. Async AutoSuggest and focus management round
out the input story.

**Phase 3 — Patterns (6-10 weeks): FUTURE**
Items 3A through 3D. Higher-level features built on the Phase 1-2 foundation.
DataGrid is a separate workstream given its size. Deferred until Parts 1-2
are shipped and validated with real usage.

**Competitive impact:** Phase 1 alone matches the competitor median (React B+,
Flutter B+). Phases 1-2 reach **A-**, ahead of every competitor. Phase 3
(future) reaches **A**, matching WPF — the first modern declarative framework
on any platform to do so.
