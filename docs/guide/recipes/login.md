
# Recipe: Login

A Microsoft.UI.Reactor (Reactor) login form is four pieces working together: validated input,
submit-gated state, an async call, and an error-display surface. The
recipe below wires them with three `UseState` hooks and a `Task` —
no view model, no event handler classes.

## Primitives

| Concern | API |
|---|---|
| Per-field state | `UseState<string>` / `UseState<bool>` |
| Submit-disabled gating | `.IsEnabled(canSubmit)` |
| Async submit | `async Task` + `Task.Delay` |
| Error display | Conditional `Empty()` vs `TextBlock` |
| Password input | [`PasswordBox`](../forms.md) |

### State

```csharp
var (email, setEmail) = UseState("");
var (pwd, setPwd) = UseState("");
var (submitting, setSubmitting) = UseState(false);
var (error, setError) = UseState<string?>(null);
```

Four `UseState` calls — input, password, in-flight flag, and the
last error. Nothing else needs to survive a re-render.

### Per-keystroke validation

```csharp
// Local validation runs on every keystroke. The submit button is
// disabled until the form is valid; in-flight submits are gated
// by the same predicate.
var emailValid = email.Contains('@') && email.Contains('.');
var pwdValid = pwd.Length >= 8;
var canSubmit = emailValid && pwdValid && !submitting;
```

The form derives `emailValid` and `pwdValid` from raw state on every
render — no debounce, no separate validation pass. Re-rendering on
every keystroke is fine in Reactor; the work happens in pure C# and
the reconciler skips slots that didn't change.

### Async submit + disabled button

```csharp
async Task Submit()
{
    setSubmitting(true);
    setError(null);
    try
    {
        await Task.Delay(800);                       // pretend API call
        if (pwd == "wrong") setError("Invalid credentials.");
    }
    finally { setSubmitting(false); }
}
```

The submit handler is a local `async Task` declared inside `Render()`.
That works because `Render()` runs on the UI thread and Reactor's
reconciler handles re-rendering when the captured setters fire.
[`UseState`](../hooks.md) setters are stable across renders, so the
captured closure keeps working.

### Render

```csharp
return VStack(12,
    Heading("Sign in"),
    TextBox(email, setEmail, placeholder: "you@example.com",
        header: "Email").Width(280),
    PasswordBox(pwd, setPwd, placeholderText: "8+ characters"),
    error is null
        ? Empty()
        : TextBlock(error).Foreground("#C42B1C"),
    Button(submitting ? "Signing in…" : "Sign in", () => _ = Submit())
        .IsEnabled(canSubmit)
).Padding(20).Width(320);
```

![Login form with inline validation](images/recipe-login/form.png)

The `canSubmit` predicate gates the button on a single render —
disabling the button is enough; an analyzer-flagged guard inside
`Submit()` would double-fire on a re-render race. The `submitting`
state owns both the spinner label ("Signing in…") and the disabled
state, so an in-flight submit can't be re-triggered by an Enter press.

## Tips

**Don't reach for a view model.** A 30-line login form doesn't need
one; the three `UseState` hooks and the local `async Task` are the
view model. The cost of a class hierarchy is the cost of maintaining
it.

**Gate at the button, not inside the handler.** A disabled button is
a single render check; a guard inside `Submit()` runs after the user
already pressed it and the UI looked ready. Both layers are good
hygiene but the button gate is the load-bearing one.

**Use [`PasswordBox`](../forms.md), not `TextBox` with a hex style.**
The control implements paste-without-reveal, autofill, and the
accessibility peer; reinventing it in user code drops those.

## Next Steps

- **[Forms](../forms.md)** — The full input + validation surface this
  recipe pulls from.
- **[Effects](../effects.md)** — Cancellation pattern when the user
  navigates away mid-submit.
- **[Recipe: Modal dialog](modal-dialog.md)** — Pair this with a
  "Forgot password?" modal.
- **[Accessibility](../accessibility.md)** — Focus order rules that
  apply once you add more fields.
- **[Recipes index](index.md)** — Back to the gallery.
