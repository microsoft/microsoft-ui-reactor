---
name: reactor-forms
description: "Reactor forms and validation — `UseValidationContext`, built-in validators (`Validate.Required`, `Validate.Email`, `Validate.MinLength`, etc.), `FormField` helper, masked input via `MaskEngine`, `InputFormatter`. Use when building data-entry screens, validation flows, or controlled-input forms."
---

# Forms and Validation in Reactor

Reactor forms use a **controlled-input pattern** — every input has an
explicit `(value, setter)` pair driven by `UseState`. There is no two-way
binding. Validation is layered on top via `UseValidationContext` and
declarative `.Validate()` modifiers.

## Quick reference

| API | Purpose |
|-----|---------|
| `TextBox(value, setValue)` | Controlled text input |
| `UseValidationContext()` | Track validation messages, touched/dirty state |
| `.Validate(...)` | Attach built-in validators to an input |
| `FormField(input, label: ...)` | Wraps input with label, error display, required marker |
| `new MaskEngine(...)` | Masked text input (phone, SSN, etc.) |
| `InputFormatter.Currency(...)` | Format-as-you-type |

## 1. Controlled inputs

Every input takes `(value, setter)`. State lives in the component:

```csharp
var (name, setName) = UseState("");
var (age, setAge) = UseState(0);
var (agreed, setAgreed) = UseState(false);

return VStack(12,
    TextBox(name, setName, placeholder: "Name"),
    NumberBox(age, setAge),
    CheckBox(agreed, setAgreed, label: "I agree"),
    Button("Submit", onSubmit).IsEnabled(!(string.IsNullOrEmpty(name) || !agreed))
);
```

### Available input types

| Factory | Value type | Common modifiers |
|---------|-----------|------------------|
| `TextBox(value, setValue, placeholder, header)` | `string` | `.Header()`, `.IsReadOnly()`, `.AcceptsReturn()`, `.TextWrapping()`, `.MaxLength(n)`, `.NumericInput()`, `.EmailInput()`, `.Changed(handler)` |
| `PasswordBox(password, setPassword, placeholder)` | `string` | `.Header(text)`, `.MaxLength(n)`, `.PasswordChanged(handler)` |
| `NumberBox(value, setValue, placeholder, header)` | `double` | `.Range(min, max)`, `.SpinButtons(...)` |
| `Slider(value, min, max, setValue)` | `double` | `.Header()`, `.StepFrequency()` |
| `ToggleSwitch(isOn, setIsOn, header, onContent, offContent)` | `bool` | `.Header()` |
| `CheckBox(isChecked, setIsChecked, label)` | `bool` | — |
| `RadioButtons(items, selected, setSelected, header)` | `int` | `.Set(rb => ...)` |
| `ComboBox(items, selected, setSelected, placeholder)` | `object` | `.Header()`, `.IsEditable()`, `.Placeholder()` |
| `DatePicker(date, setDate, header)` | `DateTimeOffset` | `.Set(dp => ...)` |
| `TimePicker(time, setTime, header)` | `TimeSpan` | `.Set(tp => ...)` |
| `AutoSuggestBox(text, setText, placeholder, header)` | `string` | `.Set(asb => ...)` |
| `RichEditBox(doc, setDoc, header)` | `string` | `.Set(reb => ...)` |
| `CalendarDatePicker(date, setDate, placeholder, header)` | `DateTimeOffset?` | `.Set(cdp => ...)` |

**Named-input shapes** (Spec 039 §17.3): `.NumericInput()` / `.EmailInput()`
preconfigure `InputScope` and IME hints so on-screen / soft keyboards open
in the right mode. Stack them with validators:

```csharp
TextBox(email, setEmail, placeholder: "you@example.com")
    .EmailInput()
    .MaxLength(254)
    .Validate("email", email, Validate.Required(), Validate.Email())
```

For modifiers that aren't in the typed surface (e.g. `PasswordRevealMode`,
date min/max), use `.Set(native => ...)` to reach the underlying WinUI control.
The full catalog is in `references/reactor.api.txt`.

## 2. Simple validation (derived booleans)

For trivial forms, derive validation from state:

```csharp
var (email, setEmail) = UseState("");
var isValid = email.Contains('@') && email.Length > 3;

return VStack(12,
    TextBox(email, setEmail, placeholder: "Email"),
    Button("Submit", onSubmit).IsEnabled(isValid)
);
```

This is fine for 1–2 fields. For anything more, use `UseValidationContext`.

## 3. UseValidationContext

Tracks per-field validation messages, touched/dirty state, and overall
form validity:

```csharp
var validation = UseValidationContext();
var (name, setName) = UseState("");
var (email, setEmail) = UseState("");

return VStack(12,
    TextBox(name, setName, placeholder: "Name")
        .Validate("name", name,
            Validate.Required("Name is required"),
            Validate.MinLength(2, "Name too short")),

    TextBox(email, setEmail, placeholder: "Email")
        .Validate("email", email,
            Validate.Required("Email is required"),
            Validate.Email("Invalid email")),

    Button("Submit", () =>
    {
        validation.MarkAllTouched();
        if (validation.IsValid())
            Submit(name, email);
    })
);
```

`.Validate(fieldName, value, ...)` resolves the surrounding `ValidationContext`
through React-style ambient context — you do not pass `validation` explicitly.
Passing the current value (the second arg) opts in to auto-validation as the
component re-renders; the validator-only overload `.Validate(fieldName, ...)`
is for cases where you trigger validation manually.

### ValidationContext API

| Member | Purpose |
|--------|---------|
| `.IsValid()` | `true` when no field has Error-severity messages |
| `.IsDirty()` | `true` when any registered field differs from initial value |
| `.IsDirty("field")` | Per-field dirty check |
| `.MarkAllTouched()` | Mark every registered field touched (typical on submit) |
| `.MarkTouched("field")` | Mark a single field touched |
| `.Reset("field")` | Reset one field to initial value, returns the initial |
| `.ResetAll()` | Reset all fields to initial values |
| `.ClearAll()` | Clear all messages (preserve touched/initial state) |
| `.GetMessages("field")` | Get error messages for a specific field |
| `.IsTouched("field")` | Whether the user has interacted with a field |

## 4. Built-in validators

The `.Validate()` modifier accepts an array of validators:

| Validator | Purpose |
|-----------|---------|
| `Validate.Required(msg)` | Non-empty |
| `Validate.MinLength(n, msg)` | Minimum string length |
| `Validate.MaxLength(n, msg)` | Maximum string length |
| `Validate.Email(msg)` | Email format |
| `Validate.Match(pattern, msg)` | Custom regex pattern |
| `Validate.Range(min, max, msg)` | Numeric range |
| `Validate.Must<T>(predicate, msg)` | Arbitrary predicate |
| `Validate.EqualTo<T>(value, msg)` | Fields must match (confirm password) |
| `Validate.Url(msg)` | URL format |
| `Validate.MustBeTrue(msg)` | Boolean must be true (checkboxes) |

## 5. FormField helper

`FormField` wraps an input with a label, required marker, description
text, and error display:

```csharp
var validation = UseValidationContext();
var (name, setName) = UseState("");

return FormField(
    TextBox(name, setName, placeholder: "Enter your name")
        .Validate("name", name, Validate.Required("Required")),
    label: "Full Name",
    required: true,
    description: "As it appears on your ID",
    showWhen: ShowWhen.WhenTouched  // or Always, WhenDirty, AfterFirstSubmit, Never
);
```

`ShowWhen` controls when error messages appear:
- `WhenTouched` — after the user has interacted with the field (recommended default)
- `Always` — immediately, even before user interaction
- `WhenDirty` — only after the value has changed
- `AfterFirstSubmit` — only after the first submit attempt

## 6. Masked input

`MaskEngine` restricts and formats input as the user types:

```csharp
var mask = UseMemo(() => new MaskEngine(MaskPreset.PhoneUS));
var (phone, setPhone) = UseState("");

return TextBox(phone, v => setPhone(mask.Apply(v)),
    placeholder: "(555) 555-0123");
```

### Mask presets

| Preset | Format |
|--------|--------|
| `MaskPreset.PhoneUS` | `(___) ___-____` |
| `MaskPreset.SSN` | `___-__-____` |
| `MaskPreset.ZipCode` | `_____` |
| `MaskPreset.ZipCodePlus4` | `_____-____` |
| `MaskPreset.CreditCard` | `____ ____ ____ ____` |
| `MaskPreset.Date` | `__/__/____` |

Custom masks: `new MaskEngine("AA-####")` where `A` = letter,
`#` = digit, `*` = any.

## 7. Input formatters

`InputFormatter` applies format-as-you-type transformations:

```csharp
var (amount, setAmount) = UseState("");

return TextBox(amount,
    v => setAmount(InputFormatter.Currency(symbol: "$").Format(v)),
    placeholder: "$0.00");
```

| Formatter | Effect |
|----------|--------|
| `InputFormatter.Currency(symbol: "$")` | `$1,234.56` |
| `InputFormatter.PhoneUS` | `(555) 555-0123` |
| `InputFormatter.UpperCase` | Force uppercase |
| `InputFormatter.LowerCase` | Force lowercase |
| `InputFormatter.TitleCase` | Title Case |
| `InputFormatter.MaxLength(n)` | Truncate at n chars |
| `InputFormatter.AllowOnly(regex)` | Whitelist characters |

## Critical gotchas

1. **Always use controlled inputs** — `(value, setter)` pair. There is no
   uncontrolled / two-way binding in Reactor.
2. **Call `validation.MarkAllTouched()` before submit** — when fields use the
   `.Validate(name, value, ...)` form, validators run automatically every
   render, but errors stay hidden until each field is touched. Mark all
   registered fields touched on submit so error messages reveal at once,
   then gate on `validation.IsValid()`.
3. **Use `ShowWhen.WhenTouched`** (default) — showing errors immediately on
   page load is hostile UX.
4. **MaskEngine and InputFormatter are different** — masks restrict what
   characters can be entered; formatters transform the display.
5. **Don't mix simple validation and UseValidationContext** — pick one
   approach per form.
6. **FormField handles layout and error display** — don't manually build
   error message TextBlocks when using FormField.
