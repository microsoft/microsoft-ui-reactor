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
| `TextField(value, setValue)` | Controlled text input |
| `UseValidationContext()` | Track validation messages, touched/dirty state |
| `.Validate(...)` | Attach built-in validators to an input |
| `FormField(label, input)` | Wraps input with label, error display, required marker |
| `new MaskEngine(...)` | Masked text input (phone, SSN, etc.) |
| `InputFormatter.Currency(...)` | Format-as-you-type |

## 1. Controlled inputs

Every input takes `(value, setter)`. State lives in the component:

```csharp
var (name, setName) = UseState("");
var (age, setAge) = UseState(0);
var (agreed, setAgreed) = UseState(false);

return VStack(12,
    TextField(name, setName, placeholder: "Name"),
    NumberBox(age, setAge),
    CheckBox("I agree", agreed, setAgreed),
    Button("Submit", onSubmit).Disabled(string.IsNullOrEmpty(name) || !agreed)
);
```

### Available input types

| Factory | Value type | Common modifiers |
|---------|-----------|------------------|
| `TextField(text, setText)` | `string` | `.Placeholder()`, `.MaxLength()` |
| `PasswordBox(text, setText)` | `string` | `.Placeholder()`, `.PasswordRevealMode()` |
| `NumberBox(value, setValue)` | `double` | `.Min()`, `.Max()`, `.SmallChange()` |
| `Slider(value, min, max, setValue)` | `double` | `.StepFrequency()` |
| `ToggleSwitch(isOn, setIsOn)` | `bool` | `.OnContent()`, `.OffContent()` |
| `CheckBox(label, isChecked, setIsChecked)` | `bool` | — |
| `RadioButtons(items, selected, setSelected)` | `int` | `.Header()` |
| `ComboBox(items, selected, setSelected)` | `object` | `.Placeholder()` |
| `DatePicker(date, setDate)` | `DateTimeOffset` | `.MinYear()`, `.MaxYear()` |
| `TimePicker(time, setTime)` | `TimeSpan` | `.MinuteIncrement()` |
| `AutoSuggestBox(text, setText)` | `string` | `.ItemsSource()`, `.OnSuggestionChosen()` |
| `RichEditBox(doc, setDoc)` | `string` | `.IsReadOnly()` |
| `CalendarDatePicker(date, setDate)` | `DateTimeOffset?` | `.MinDate()`, `.MaxDate()` |

## 2. Simple validation (derived booleans)

For trivial forms, derive validation from state:

```csharp
var (email, setEmail) = UseState("");
var isValid = email.Contains('@') && email.Length > 3;

return VStack(12,
    TextField(email, setEmail, placeholder: "Email"),
    Button("Submit", onSubmit).Disabled(!isValid)
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
    TextField(name, setName, placeholder: "Name")
        .Validate(validation, "name",
            Validate.Required("Name is required"),
            Validate.MinLength(2, "Name too short")),

    TextField(email, setEmail, placeholder: "Email")
        .Validate(validation, "email",
            Validate.Required("Email is required"),
            Validate.Email("Invalid email")),

    Button("Submit", () =>
    {
        validation.ValidateAll();
        if (validation.IsValid)
            Submit(name, email);
    })
);
```

### ValidationContext API

| Member | Purpose |
|--------|---------|
| `.IsValid` | `true` when no field has errors |
| `.IsDirty` | `true` when any field differs from initial value |
| `.ValidateAll()` | Force validation on all registered fields |
| `.Reset()` | Clear all messages and touched/dirty flags |
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

return FormField("Full Name",
    TextField(name, setName, placeholder: "Enter your name")
        .Validate(validation, "name", Validate.Required("Required")),
    required: true,
    description: "As it appears on your ID",
    showWhen: ShowWhen.WhenTouched  // or Always, WhenDirty, AfterFirstSubmit
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

return TextField(phone, v => setPhone(mask.Apply(v)),
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

return TextField(amount,
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
2. **Call `validation.ValidateAll()` before submit** — individual fields
   validate on blur/change, but you must trigger all-field validation
   before acting on the form.
3. **Use `ShowWhen.WhenTouched`** (default) — showing errors immediately on
   page load is hostile UX.
4. **MaskEngine and InputFormatter are different** — masks restrict what
   characters can be entered; formatters transform the display.
5. **Don't mix simple validation and UseValidationContext** — pick one
   approach per form.
6. **FormField handles layout and error display** — don't manually build
   error message TextBlocks when using FormField.
