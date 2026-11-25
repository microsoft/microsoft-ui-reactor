# Modifiers: Validation

Validators, required fields and error styling.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Forms and Input](../../forms.md)

## `GetValidation`

Gets the ValidationAttached metadata from an element, if any.

```csharp
ValidationAttached? GetValidation<T>(this T el) where T : Element
```

Declared in `ValidateExtensions`.

**Applies to:** any element.

## `Immediate`

Requests that the control commit its value on every keystroke instead of on blur. Has no effect on controls whose default already fires per-keystroke (TextBox, PasswordBox, AutoSuggestBox, etc.).

```csharp
T Immediate<T>(this T el) where T : Element
```

Declared in `ImmediateExtensions`.

**Applies to:** any element.

## `OnError`

Configures custom styling to apply when the field has an error. The configure action receives the element builder and can set any modifier.

```csharp
T OnError<T>(this T el, Func<T, T> configure) where T : Element
```

Declared in `ErrorStylingExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `configure` | `Func<T, T>` |  |

## `OnWarning`

Configures custom styling to apply when the field has a warning.

```csharp
T OnWarning<T>(this T el, Func<T, T> configure) where T : Element
```

Declared in `ErrorStylingExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `configure` | `Func<T, T>` |  |

## `Required`

Sets AutomationProperties.IsRequiredForForm. Screen readers announce "required".

```csharp
T Required<T>(this T el) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Example**

```csharp
TextBox(name, setName).Required()
```

## `ShowErrors`

Wraps this element in an inline visualizer that shows error text below the control.

```csharp
ValidationVisualizerElement ShowErrors<T>(this T el, ShowWhen showWhen = ShowWhen.Always) where T : Element
```

Declared in `ShowErrorsExtension`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `showWhen` | [`ShowWhen`](../validation/enums.md#showwhen) | `ShowWhen.Always` |  |

## `Validate`

```csharp
T Validate<T>(this T el, string fieldName, params IValidator[] validators) where T : Element
T Validate<T>(this T el, string fieldName, object? value, params IValidator[] validators) where T : Element
```

Declared in `ValidateExtensions`.

| Overload | Description |
| --- | --- |
| `Validate<T>(T, string, IValidator[])` | Attaches validators to this element. The element's value will be validated against the provided validators, with results pushed to the nearest ValidationContext. |
| `Validate<T>(T, string, object?, IValidator[])` | Attaches validators to this element along with the current field value, and — when called from inside a component's `Render()` — runs them immediately against the enclosing [`ValidationContext`](../validation/ValidationContext.md).  Running during render rather than during reconcile is what makes the results readable by the same `Render()` that produced them, so `When(ctx.IsTouched(f) && ctx.HasError(f), …)` placed after this call sees the current verdict instead of the previous pass's.  The validators are still attached, so `FormField` keeps working for elements built outside a render pass. Re-running them is harmless: results are applied with a structural diff. The visualizers only \*display\* what a context already holds — they never run attached validators — so an element that reaches neither a render pass nor a `FormField` contributes no verdict. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` | Field name for validation messages. |
| `validators` | [`IValidator[]`](../validation/IValidator.md) | One or more validators to apply. |
| `value` | `object?` |  |

## `ValidateAsync`

```csharp
T ValidateAsync<T>(this T el, string fieldName, params IAsyncValidator[] asyncValidators) where T : Element
T ValidateAsync<T>(this T el, string fieldName, object? value, params IAsyncValidator[] asyncValidators) where T : Element
```

Declared in `ValidateExtensions`.

| Overload | Description |
| --- | --- |
| `ValidateAsync<T>(T, string, IAsyncValidator[])` | Attaches async validators to this element (in addition to any sync validators). |
| `ValidateAsync<T>(T, string, object?, IAsyncValidator[])` | Attaches async validators to this element along with the current field value, and registers the field so `MarkAllTouched()` covers it.  This overload is **attach-only**: it does not run the validators. Nothing consumes [`ValidationAttached.AsyncValidators`](../validation/attached.md#asyncvalidators) automatically — not the render scope, which must stay synchronous, and not `FormField`. Run them yourself from an effect via [`ValidationReconciler.ValidateFieldAsync`](../validation/ValidationReconciler.md#validatefieldasync), which carries the generation guard that discards a result superseded by a newer value.  Registration happens here for an element built during a render, and again when `FormField` mounts or updates it, which is the only chance an element assembled outside a render pass gets. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |
| `asyncValidators` | [`IAsyncValidator[]`](../validation/IAsyncValidator.md) |  |
| `value` | `object?` |  |

## `WithErrorStyling`

Applies error styling to an element based on ValidationContext state. Returns the element with appropriate border styling applied.

```csharp
T WithErrorStyling<T>(this T el, ValidationContext ctx, string fieldName, ShowWhen showWhen = ShowWhen.WhenTouched, bool submitAttempted = false) where T : Element
```

Declared in `ErrorStylingExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext`](../validation/ValidationContext.md) |  |  |
| `fieldName` | `string` |  |  |
| `showWhen` | [`ShowWhen`](../validation/enums.md#showwhen) | `ShowWhen.WhenTouched` |  |
| `submitAttempted` | `bool` | `false` |  |
