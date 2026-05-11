
# Forms and Input

Every form control in Reactor follows the controlled-input pattern: you own the
value, you provide the change handler, and the control reflects your state.
There is no two-way binding. The data always flows one direction.

## The Controlled-Input Pattern

Pass the current value and a setter. When the user types, `onChange` fires
with the new value. You call the setter, Reactor re-renders, and the control
shows the updated text:

```csharp
class ControlledInputDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");

        return VStack(12,
            SubHeading("Controlled Input"),
            TextField(name, setName, placeholder: "Type your name"),
            TextBlock($"You typed: {name}").Opacity(0.6)
        ).Padding(24);
    }
}
```

![Controlled input](images/forms/controlled-input.png)

This is the same [`UseState`](hooks.md) pattern from [Getting Started](getting-started.md),
applied to form inputs. The control never holds its own state — your component
is the single source of truth.

## Input Control Types

Reactor provides controls for every common input type. Each follows the same
pattern: current value in, change handler out.

```csharp
class InputTypesDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        var (password, setPassword) = UseState("");
        var (volume, setVolume) = UseState(50.0);
        var (count, setCount) = UseState(1.0);
        var (agree, setAgree) = UseState(false);
        var (notify, setNotify) = UseState(true);
        var (role, setRole) = UseState(0);
        var (priority, setPriority) = UseState(0);

        return VStack(12,
            TextField(text, setText, placeholder: "Email",
                header: "Email"),
            PasswordBox(password, setPassword,
                placeholderText: "Enter password"),
            Slider(volume, 0, 100, setVolume),
            NumberBox(count, setCount, header: "Quantity"),
            CheckBox(agree, setAgree, label: "I agree to the terms"),
            ToggleSwitch(notify, setNotify,
                header: "Notifications"),
            ComboBox(["Admin", "Editor", "Viewer"],
                role, setRole),
            RadioButtons(["Low", "Medium", "High"],
                priority, setPriority)
        ).Padding(24);
    }
}
```

![All input types](images/forms/input-types.png)

| Control | Value type | Change handler |
|---------|-----------|---------------|
| `TextField` | `string` | `Action<string>` |
| `PasswordBox` | `string` | `Action<string>` |
| `Slider` | `double` | `Action<double>` |
| `NumberBox` | `double` | `Action<double>` |
| `CheckBox` | `bool` | `Action<bool>` |
| `ToggleSwitch` | `bool` | `Action<bool>` |
| `ComboBox` | `int` (index) | `Action<int>` |
| `RadioButtons` | `int` (index) | `Action<int>` |

All controls accept optional parameters for labels, headers, and placeholder
text. Check the API reference for each control's full signature.

## Simple Validation

For quick forms, derive validation from state. Compute booleans on every
render, show inline error messages, and re-check the booleans inside the
submit handler:

```csharp
class ValidationDemo : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);
        var (showErrors, setShowErrors) = UseState(false);

        var emailValid = email.Contains('@') && email.Contains('.');
        var ageValid = age >= 18 && age <= 120;
        var formValid = emailValid && ageValid
            && !string.IsNullOrWhiteSpace(email);

        return VStack(12,
            SubHeading("Simple Validation"),
            TextField(email, setEmail, placeholder: "user@example.com",
                header: "Email"),
            When(!string.IsNullOrEmpty(email) && !emailValid, () =>
                TextBlock("Enter a valid email address")
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            NumberBox(age, setAge, header: "Age"),
            When((showErrors || age > 0) && !ageValid, () =>
                TextBlock("Age must be between 18 and 120")
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            Button("Submit", () =>
            {
                setShowErrors(true);
                if (!formValid) return;
                // submit...
            }).Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}
```

![Validation demo](images/forms/validation.png)

This works well for small forms. For larger forms with cross-field rules,
touched/dirty tracking, and error display policies, use the validation
framework described next.

## Keeping Submit Reachable

A common form pattern — *disable the Submit button until the form is valid* —
is a keyboard accessibility trap when combined with controls like `NumberBox`,
`DatePicker`, and `CalendarDatePicker`, which commit their value on blur
rather than per keystroke. The user fixes the last invalid field, presses
Tab to reach Submit, and focus skips the still-disabled button. By the time
the field commits and Submit becomes enabled, focus has already moved on.
The user is stranded with no keyboard route to a button they can now press.

Reactor offers two opt-in fixes for this. They compose:

```csharp
class KeepSubmitReachableDemo : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);

        var emailValid = email.Contains('@') && email.Contains('.');
        var ageValid = age >= 18 && age <= 120;
        var formValid = emailValid && ageValid;

        return VStack(12,
            SubHeading("Keeping Submit Reachable"),
            TextField(email, setEmail, header: "Email",
                placeholder: "user@example.com"),

            // .Immediate() switches NumberBox from commit-on-blur to
            // commit-on-keystroke, so validation reacts as the user types.
            NumberBox(age, setAge, header: "Age").Immediate(),

            // .DisabledFocusable() keeps the button tab-reachable and
            // visually dimmed while preventing invocation. Pattern mirrors
            // Fluent UI's `disabledFocusable` and ARIA `aria-disabled`.
            Button("Submit", () => { /* submit */ })
                .DisabledFocusable(!formValid)
                .Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}
```

![Keep submit reachable](images/forms/keep-submit-reachable.png)

**`.DisabledFocusable(bool)` on Button** keeps the button keyboard-focusable
and tab-reachable while presenting it as disabled (dimmed; the click is
suppressed). Mirrors Fluent UI React's `disabledFocusable` and ARIA's
`aria-disabled`. Use it for any Submit gated on validation, busy state, or
other derived conditions.

**`.Immediate()` on NumberBox** (in `Microsoft.UI.Reactor.Controls.Validation`)
switches the control from commit-on-blur to commit-on-keystroke, so
validation reacts to every parseable digit instead of waiting for focus to
leave the field. Opt-in by design — commit-on-blur is the WinUI default and
the right choice when an intermediate value would be expensive or surprising
(snapping `2.50` to `2.5` mid-edit). Apply when validation gates UI state
and you want it to feel live.

Use `.DisabledFocusable()` whenever a button is conditionally disabled in a
form — even if you've also applied `.Immediate()` to every commit-on-blur
input. The two cover different failure modes: `.Immediate()` keeps validity
in sync with typing; `.DisabledFocusable()` keeps the button discoverable
when validity is gated on async checks, required-but-untouched fields,
cross-field rules, or any derived condition that can't be made instantaneous.

> **Where not to use `.DisabledFocusable()`:** only buttons. For data-entry
> controls (`TextField`, `NumberBox`, `CheckBox`, etc.), `IsEnabled=false`
> usually means "this field isn't part of your current task" (cascading
> from another input), and tab-skipping is the correct UX. Use
> `IsReadOnly` if you need a visible-but-non-editable text control.

## Validation Context

`UseValidationContext()` creates a `ValidationContext` that tracks messages,
touched/dirty state, and field registration. Attach validators to controls
with `.Validate()`:

```csharp
class ValidationContextDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (email, setEmail) = UseState("");
        var (password, setPassword) = UseState("");
        var (submitted, setSubmitted) = UseState(false);

        return VStack(12,
            SubHeading("Validation Context"),
            TextField(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); },
                placeholder: "user@example.com", header: "Email")
                .Validate("email", email,
                    Validate.Required(),
                    Validate.Email()),
            When(ctx.IsTouched("email") && ctx.HasError("email"), () =>
                TextBlock(ctx.GetMessages("email").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            PasswordBox(password, v => { setPassword(v); ctx.NotifyValueChanged("password", v); },
                placeholderText: "Min 8 characters")
                .Validate("password", password,
                    Validate.Required(),
                    Validate.MinLength(8)),
            When(ctx.IsTouched("password") && ctx.HasError("password"), () =>
                TextBlock(ctx.GetMessages("password").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            Button("Register", () =>
            {
                ctx.MarkAllTouched();
                if (ctx.IsValid()) setSubmitted(true);
            }).Disabled(submitted),
            When(submitted, () =>
                TextBlock("Registration successful!")
                    .Foreground(Theme.SystemSuccess).SemiBold())
        ).Padding(24);
    }
}
```

![Validation context](images/forms/validation-context.png)

Key pieces:

- **`UseValidationContext()`** creates or retrieves the nearest validation context.
- **`.Validate(fieldName, value, validators...)`** attaches validators to a control.
- **`Validate.Required()`, `Validate.Email()`, etc.** are the 11 built-in validators.
- **`ctx.IsValid()`** returns `true` when no error-severity messages exist.
- **`ctx.MarkAllTouched()`** reveals all errors on submit attempt.

## FormField Helper

`FormField()` wraps a control with a label, required indicator, description
text, and inline error display:

```csharp
class FormFieldDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");

        return VStack(12,
            SubHeading("FormField Helper"),
            FormField(
                TextField(name, v => { setName(v); ctx.NotifyValueChanged("name", v); })
                    .Validate("name", name, Validate.Required()),
                label: "Full Name",
                required: true,
                description: "As it appears on your ID"),
            FormField(
                TextField(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); })
                    .Validate("email", email,
                        Validate.Required(), Validate.Email()),
                label: "Email Address",
                required: true)
        ).Padding(24);
    }
}
```

![FormField helper](images/forms/form-field.png)

`FormField` auto-detects the field name from the `.Validate()` attachment on
its content. Errors appear below the field after the field is touched (focus
then blur). The `ShowWhen` parameter controls when errors become visible:
`WhenTouched` (default), `WhenDirty`, `AfterFirstSubmit`, `Always`, or `Never`.

## Built-in Validators

| Validator | Purpose |
|----------|---------|
| `Validate.Required()` | Non-null, non-empty, non-default |
| `Validate.MinLength(n)` | String length >= n |
| `Validate.MaxLength(n)` | String length <= n |
| `Validate.Range(min, max)` | Numeric value in range |
| `Validate.Match(regex)` | Regex pattern match |
| `Validate.Email()` | Valid email address |
| `Validate.Url()` | Valid URL (http/https) |
| `Validate.Must<T>(predicate, message)` | Custom predicate |
| `Validate.MustAsync<T>(predicate, message)` | Async predicate |
| `Validate.MustBeTrue()` | Boolean is true (checkboxes) |
| `Validate.EqualTo<T>(value)` | Equality check (password confirm) |

Every validator accepts an optional custom error message as its last parameter.

## Masked Input

`MaskEngine` applies input masks with auto-inserted literals. Use the built-in
presets or define your own pattern:

```csharp
class MaskedInputDemo : Component
{
    public override Element Render()
    {
        var phoneMask = UseMemo(() => new MaskEngine(MaskPreset.PhoneUS));
        var dateMask = UseMemo(() => new MaskEngine(MaskPreset.Date));
        var (phone, setPhone) = UseState("");
        var (date, setDate) = UseState("");

        return VStack(12,
            SubHeading("Masked Input"),
            TextField(phoneMask.Apply(phone), v => setPhone(phoneMask.GetRawValue(v)),
                placeholder: "(___) ___-____", header: "Phone"),
            TextBlock($"Raw: {phone}").FontSize(12).Opacity(0.6),
            TextField(dateMask.Apply(date), v => setDate(dateMask.GetRawValue(v)),
                placeholder: "__/__/____", header: "Date"),
            TextBlock($"Raw: {date}").FontSize(12).Opacity(0.6)
        ).Padding(24);
    }
}
```

![Masked input](images/forms/masked-input.png)

Mask tokens: `0` = required digit, `9` = optional digit, `A` = required
letter, `a` = optional letter, `*` = required alphanumeric. All other
characters are literals inserted automatically.

| Preset | Pattern |
|--------|---------|
| `MaskPreset.PhoneUS` | `(000) 000-0000` |
| `MaskPreset.SSN` | `000-00-0000` |
| `MaskPreset.CreditCard` | `0000 0000 0000 0000` |
| `MaskPreset.Date` | `00/00/0000` |
| `MaskPreset.ZipCode` | `00000` |
| `MaskPreset.IPv4` | `099.099.099.099` |

## Input Formatters

`InputFormatter` transforms text as the user types. Chain formatters into a
pipeline for complex formatting:

```csharp
class InputFormattersDemo : Component
{
    public override Element Render()
    {
        var (currency, setCurrency) = UseState("");
        var (upper, setUpper) = UseState("");

        var currencyFmt = UseMemo(() => InputFormatter.Currency());
        var upperFmt = UseMemo(() => InputFormatter.UpperCase);

        return VStack(12,
            SubHeading("Input Formatters"),
            TextField(currencyFmt.Format(currency, 0).Output,
                v => setCurrency(currencyFmt.Parse(v)),
                placeholder: "$0.00", header: "Amount"),
            TextField(upperFmt.Format(upper, 0).Output,
                v => setUpper(upperFmt.Parse(v)),
                placeholder: "UPPERCASE", header: "Code")
        ).Padding(24);
    }
}
```

![Input formatters](images/forms/input-formatters.png)

Built-in formatters: `PhoneUS`, `Currency()`, `UpperCase`, `LowerCase`,
`TitleCase`, `TrimWhitespace`, `MaxLength(n)`, `AllowOnly(regex)`,
`DenyOnly(regex)`, and `Custom(format, parse)`.

## Tips

**Always use controlled inputs.** Never let a control manage its own state.
Your `Render()` method is the single source of truth — if you need to
pre-fill, validate, or reset a field, you just set the state value.

**Use `FormField` for production forms.** It handles labels, required
indicators, and error display with consistent styling. Avoid building your
own field wrapper.

**Use `Validate.Must<T>()` for custom rules.** When built-in validators
don't cover your case, `Must` takes any `Func<T, bool>` predicate.

**Call `ctx.MarkAllTouched()` on submit.** This reveals all validation errors
at once, so the user sees everything that needs fixing.

**Reset forms with `ctx.ResetAll()`.** It returns all fields to their initial
values, clears touched/dirty state, and removes all messages.

## Next Steps

- **[Flex Layout](flex-layout.md)** — Previous: flexible box layout for adaptive UIs
- **[Collections](collections.md)** — Next: render lists, grids, and virtualized data sets
- **[Hooks](hooks.md)** — UseState, UseMemo, and other hooks that power form logic
- **[Commanding](commanding.md)** — wire submit buttons to async commands with busy/error handling
- **[Styling and Theming](styling.md)** — theme your forms with tokens and lightweight styling
