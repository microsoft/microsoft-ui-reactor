---
title: "Masked input"
description: "The mask engine and formatters behind MaskedTextBox."
---

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls)

## `FormatResult` [#formatresult]

Result returned by an [`InputFormatter`](#inputformatter): the transformed output text paired with the adjusted cursor position.

```csharp
public readonly record struct FormatResult(string Output, int CursorPos)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors [#constructors]

#### `FormatResult` [#formatresult_1]

Result returned by an [`InputFormatter`](#inputformatter): the transformed output text paired with the adjusted cursor position.

```csharp
public FormatResult(string Output, int CursorPos)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Output` | `string` | The transformed output text. |
| `CursorPos` | `int` | The cursor position in the output. |

### Properties [#properties]

#### `CursorPos` [#cursorpos]

The cursor position in the output.

```csharp
public int CursorPos  // get, init
```

#### `Output` [#output]

The transformed output text.

```csharp
public string Output  // get, init
```

## `FormatterPipeline` [#formatterpipeline]

Chains multiple formatters into a pipeline.

```csharp
public sealed class FormatterPipeline
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors [#constructors_1]

#### `FormatterPipeline` [#formatterpipeline_1]

```csharp
public FormatterPipeline(params InputFormatter[] formatters)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatters` | [`InputFormatter[]`](#inputformatter) |  |

### Methods [#methods]

#### `Format` [#format]

Runs all formatters in sequence.

```csharp
public FormatResult Format(string input, int cursorPos)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `input` | `string` |  |
| `cursorPos` | `int` |  |

#### `Parse` [#parse]

Parses a formatted value through all formatters in reverse order.

```csharp
public string Parse(string formatted)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatted` | `string` |  |

## `InputFormatter` [#inputformatter]

Base class for input formatters that transform text as the user types.

```csharp
public abstract class InputFormatter
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors [#constructors_2]

#### `InputFormatter` [#inputformatter_1]

```csharp
protected InputFormatter()
```

### Properties [#properties_1]

#### `LowerCase` [#lowercase]

Force lowercase.

```csharp
public static InputFormatter LowerCase  // get
```

#### `PhoneUS` [#phoneus]

US phone format: (555) 123-4567

```csharp
public static InputFormatter PhoneUS  // get
```

#### `TitleCase` [#titlecase]

Title case (first letter of each word uppercase).

```csharp
public static InputFormatter TitleCase  // get
```

#### `TrimWhitespace` [#trimwhitespace]

Trim leading and trailing whitespace.

```csharp
public static InputFormatter TrimWhitespace  // get
```

#### `UpperCase` [#uppercase]

Force uppercase.

```csharp
public static InputFormatter UpperCase  // get
```

### Methods [#methods_1]

#### `AllowOnly` [#allowonly]

Only allow characters matching the regex.

```csharp
public static InputFormatter AllowOnly(string regex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `regex` | `string` |  |

#### `Currency` [#currency]

Currency format: $1,234.56

```csharp
public static InputFormatter Currency(string symbol = "$")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `symbol` | `string` | `"$"` |  |

#### `Custom` [#custom]

Custom formatter with format and parse functions.

```csharp
public static InputFormatter Custom(Func<string, string> format, Func<string, string> parse)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `format` | `Func<string, string>` |  |
| `parse` | `Func<string, string>` |  |

#### `DenyOnly` [#denyonly]

Remove characters matching the regex.

```csharp
public static InputFormatter DenyOnly(string regex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `regex` | `string` |  |

#### `Format` [#format_1]

Formats the input and adjusts cursor position.

```csharp
public abstract FormatResult Format(string input, int cursorPos)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `input` | `string` |  |
| `cursorPos` | `int` |  |

#### `MaxLength` [#maxlength]

Truncate at n characters.

```csharp
public static InputFormatter MaxLength(int n)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `n` | `int` |  |

#### `Parse` [#parse_1]

Parses a formatted value back to a raw model value. Default implementation returns the formatted value as-is.

```csharp
public virtual string Parse(string formatted)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatted` | `string` |  |

#### `PhoneIntl` [#phoneintl]

International phone with configurable country code.

```csharp
public static InputFormatter PhoneIntl(string countryCode = "+1")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `countryCode` | `string` | `"+1"` |  |

## `MaskEngine` [#maskengine]

Engine that parses mask patterns and applies them to input strings.

Mask tokens: 0 = required digit 9 = optional digit A = required letter a = optional letter \* = required alphanumeric All others = literal (auto-insert)

```csharp
public sealed class MaskEngine
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors [#constructors_3]

#### `MaskEngine` [#maskengine_1]

```csharp
public MaskEngine(string mask)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mask` | `string` |  |

### Properties [#properties_2]

#### `Length` [#length]

Number of tokens in the mask.

```csharp
public int Length  // get
```

#### `Mask` [#mask]

The original mask pattern.

```csharp
public string Mask  // get
```

### Methods [#methods_2]

#### `Apply` [#apply]

Applies the mask to raw input, producing a formatted output. Literal characters are auto-inserted at the correct positions.

```csharp
public string Apply(string input, char placeholder = '_')
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `input` | `string` |  |  |
| `placeholder` | `char` | `'_'` |  |

#### `GetRawValue` [#getrawvalue]

Extracts the raw value from a formatted string by stripping literals and placeholders.

```csharp
public string GetRawValue(string formatted, char placeholder = '_')
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `formatted` | `string` |  |  |
| `placeholder` | `char` | `'_'` |  |

#### `IsComplete` [#iscomplete]

Returns true if all required slots are filled.

```csharp
public bool IsComplete(string formatted, char placeholder = '_')
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `formatted` | `string` |  |  |
| `placeholder` | `char` | `'_'` |  |

#### `IsLiteral` [#isliteral]

Returns true if the position is a literal position.

```csharp
public bool IsLiteral(int position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |

#### `NextInputPosition` [#nextinputposition]

Finds the next non-literal position at or after the given position.

```csharp
public int NextInputPosition(int position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |

#### `PreviousInputPosition` [#previousinputposition]

Finds the previous non-literal position at or before the given position.

```csharp
public int PreviousInputPosition(int position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `int` |  |

## `MaskPreset` [#maskpreset]

Common mask presets for standard formats.

```csharp
public static class MaskPreset
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Fields [#fields]

#### `CreditCard` [#creditcard]

Credit card: 0000 0000 0000 0000

```csharp
public const string CreditCard = "0000 0000 0000 0000"
```

#### `Date` [#date]

Date: 00/00/0000

```csharp
public const string Date = "00/00/0000"
```

#### `IPv4` [#ipv4]

IPv4: 099.099.099.099

```csharp
public const string IPv4 = "099.099.099.099"
```

#### `PhoneUS` [#phoneus_1]

US phone: (000) 000-0000

```csharp
public const string PhoneUS = "(000) 000-0000"
```

#### `SSN` [#ssn]

SSN: 000-00-0000

```csharp
public const string SSN = "000-00-0000"
```

#### `Time` [#time]

Time: 00:00

```csharp
public const string Time = "00:00"
```

#### `ZipCode` [#zipcode]

ZIP code: 00000

```csharp
public const string ZipCode = "00000"
```

#### `ZipCodePlus4` [#zipcodeplus4]

ZIP+4: 00000-0000

```csharp
public const string ZipCodePlus4 = "00000-0000"
```
