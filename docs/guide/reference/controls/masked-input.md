# Masked input

The mask engine and formatters behind MaskedTextBox.

**Learn more:** [Controls](../../controls.md)

## `FormatResult`

Result returned by an [`InputFormatter`](#inputformatter): the transformed output text paired with the adjusted cursor position.

```csharp
public readonly record struct FormatResult(string Output, int CursorPos)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors

#### `FormatResult`

Result returned by an [`InputFormatter`](#inputformatter): the transformed output text paired with the adjusted cursor position.

```csharp
public FormatResult(string Output, int CursorPos)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Output` | `string` | The transformed output text. |
| `CursorPos` | `int` | The cursor position in the output. |

### Properties

#### `CursorPos`

The cursor position in the output.

```csharp
public int CursorPos  // get, init
```

#### `Output`

The transformed output text.

```csharp
public string Output  // get, init
```

## `FormatterPipeline`

Chains multiple formatters into a pipeline.

```csharp
public sealed class FormatterPipeline
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors

#### `FormatterPipeline`

```csharp
public FormatterPipeline(params InputFormatter[] formatters)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatters` | [`InputFormatter[]`](#inputformatter) |  |

### Methods

#### `Format`

Runs all formatters in sequence.

```csharp
public FormatResult Format(string input, int cursorPos)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `input` | `string` |  |
| `cursorPos` | `int` |  |

#### `Parse`

Parses a formatted value through all formatters in reverse order.

```csharp
public string Parse(string formatted)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatted` | `string` |  |

## `InputFormatter`

Base class for input formatters that transform text as the user types.

```csharp
public abstract class InputFormatter
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors

#### `InputFormatter`

```csharp
protected InputFormatter()
```

### Properties

#### `LowerCase`

Force lowercase.

```csharp
public static InputFormatter LowerCase  // get
```

#### `PhoneUS`

US phone format: (555) 123-4567

```csharp
public static InputFormatter PhoneUS  // get
```

#### `TitleCase`

Title case (first letter of each word uppercase).

```csharp
public static InputFormatter TitleCase  // get
```

#### `TrimWhitespace`

Trim leading and trailing whitespace.

```csharp
public static InputFormatter TrimWhitespace  // get
```

#### `UpperCase`

Force uppercase.

```csharp
public static InputFormatter UpperCase  // get
```

### Methods

#### `AllowOnly`

Only allow characters matching the regex.

```csharp
public static InputFormatter AllowOnly(string regex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `regex` | `string` |  |

#### `Currency`

Currency format: $1,234.56

```csharp
public static InputFormatter Currency(string symbol = "$")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `symbol` | `string` | `"$"` |  |

#### `Custom`

Custom formatter with format and parse functions.

```csharp
public static InputFormatter Custom(Func<string, string> format, Func<string, string> parse)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `Func<string, string>` |  |
| `parse` | `Func<string, string>` |  |

#### `DenyOnly`

Remove characters matching the regex.

```csharp
public static InputFormatter DenyOnly(string regex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `regex` | `string` |  |

#### `Format`

Formats the input and adjusts cursor position.

```csharp
public abstract FormatResult Format(string input, int cursorPos)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `input` | `string` |  |
| `cursorPos` | `int` |  |

#### `MaxLength`

Truncate at n characters.

```csharp
public static InputFormatter MaxLength(int n)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `n` | `int` |  |

#### `Parse`

Parses a formatted value back to a raw model value. Default implementation returns the formatted value as-is.

```csharp
public virtual string Parse(string formatted)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatted` | `string` |  |

#### `PhoneIntl`

International phone with configurable country code.

```csharp
public static InputFormatter PhoneIntl(string countryCode = "+1")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `countryCode` | `string` | `"+1"` |  |

## `MaskEngine`

Engine that parses mask patterns and applies them to input strings.

Mask tokens: 0 = required digit 9 = optional digit A = required letter a = optional letter \* = required alphanumeric All others = literal (auto-insert)

```csharp
public sealed class MaskEngine
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors

#### `MaskEngine`

```csharp
public MaskEngine(string mask)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mask` | `string` |  |

### Properties

#### `Length`

Number of tokens in the mask.

```csharp
public int Length  // get
```

#### `Mask`

The original mask pattern.

```csharp
public string Mask  // get
```

### Methods

#### `Apply`

Applies the mask to raw input, producing a formatted output. Literal characters are auto-inserted at the correct positions.

```csharp
public string Apply(string input, char placeholder = '_')
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `input` | `string` |  |  |
| `placeholder` | `char` | `'_'` |  |

#### `GetRawValue`

Extracts the raw value from a formatted string by stripping literals and placeholders.

```csharp
public string GetRawValue(string formatted, char placeholder = '_')
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `formatted` | `string` |  |  |
| `placeholder` | `char` | `'_'` |  |

#### `IsComplete`

Returns true if all required slots are filled.

```csharp
public bool IsComplete(string formatted, char placeholder = '_')
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `formatted` | `string` |  |  |
| `placeholder` | `char` | `'_'` |  |

#### `IsLiteral`

Returns true if the position is a literal position.

```csharp
public bool IsLiteral(int position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |

#### `NextInputPosition`

Finds the next non-literal position at or after the given position.

```csharp
public int NextInputPosition(int position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |

#### `PreviousInputPosition`

Finds the previous non-literal position at or before the given position.

```csharp
public int PreviousInputPosition(int position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |

## `MaskPreset`

Common mask presets for standard formats.

```csharp
public static class MaskPreset
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Fields

#### `CreditCard`

Credit card: 0000 0000 0000 0000

```csharp
public const string CreditCard = "0000 0000 0000 0000"
```

#### `Date`

Date: 00/00/0000

```csharp
public const string Date = "00/00/0000"
```

#### `IPv4`

IPv4: 099.099.099.099

```csharp
public const string IPv4 = "099.099.099.099"
```

#### `PhoneUS`

US phone: (000) 000-0000

```csharp
public const string PhoneUS = "(000) 000-0000"
```

#### `SSN`

SSN: 000-00-0000

```csharp
public const string SSN = "000-00-0000"
```

#### `Time`

Time: 00:00

```csharp
public const string Time = "00:00"
```

#### `ZipCode`

ZIP code: 00000

```csharp
public const string ZipCode = "00000"
```

#### `ZipCodePlus4`

ZIP+4: 00000-0000

```csharp
public const string ZipCodePlus4 = "00000-0000"
```
