# Attached validation state

Records that hold the validation state stored on an element.

**Learn more:** [Forms and Input](../../forms.md)

## `ErrorStylingAttached`

Attached metadata for custom error/warning styling transforms.

```csharp
public sealed record ErrorStylingAttached(Func<Element, Element>? OnErrorTransform = null, Func<Element, Element>? OnWarningTransform = null)
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

### Constructors

#### `ErrorStylingAttached`

Attached metadata for custom error/warning styling transforms.

```csharp
public ErrorStylingAttached(Func<Element, Element>? OnErrorTransform = null, Func<Element, Element>? OnWarningTransform = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `OnErrorTransform` | `Func<Element, Element>?` | `null` |  |
| `OnWarningTransform` | `Func<Element, Element>?` | `null` |  |

### Properties

#### `OnErrorTransform`

```csharp
public Func<Element, Element>? OnErrorTransform  // get, init
```

#### `OnWarningTransform`

```csharp
public Func<Element, Element>? OnWarningTransform  // get, init
```

### Fields

#### `Default`

```csharp
public static readonly ErrorStylingAttached Default
```

## `ValidationAttached`

Attached validation metadata for an element, stored in Element.Attached.

```csharp
public sealed record ValidationAttached(string FieldName, IValidator[] Validators, IAsyncValidator[] AsyncValidators)
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

### Constructors

#### `ValidationAttached`

Attached validation metadata for an element, stored in Element.Attached.

```csharp
public ValidationAttached(string FieldName, IValidator[] Validators, IAsyncValidator[] AsyncValidators)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `FieldName` | `string` |  |
| `Validators` | [`IValidator[]`](IValidator.md) |  |
| `AsyncValidators` | [`IAsyncValidator[]`](IAsyncValidator.md) |  |

### Properties

#### `AsyncValidators`

```csharp
public IAsyncValidator[] AsyncValidators  // get, init
```

#### `FieldName`

```csharp
public string FieldName  // get, init
```

#### `HasValue`

True when a value overload of `.Validate()`/`.ValidateAsync()` supplied [`ValidationAttached.Value`](#value).

`null` is a legitimate value, so the property alone cannot distinguish "the field is empty" from "no value was ever attached". Without that distinction `FormField` validated `null` for the validator-only overload and reported a required-field error for a control that plainly had text in it.

```csharp
public bool HasValue  // get, init
```

#### `Validators`

```csharp
public IValidator[] Validators  // get, init
```

#### `Value`

The current field value, used for automatic validation when the element is mounted inside a `FormFieldElement`. The visualizers are display-only: they render what a context already holds and never run attached validators.

```csharp
public object? Value  // get, init
```

### Fields

#### `Empty`

```csharp
public static readonly ValidationAttached Empty
```
