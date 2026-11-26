# Enums

The enumerations in this category.

**Learn more:** [Localization](../../localization.md)

## `DateStyle`

Options for IntlAccessor.FormatDate(). Maps to .NET DateTimeFormatInfo styles.

```csharp
public enum DateStyle
```

Namespace: `Microsoft.UI.Reactor.Localization`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Default` | `0` |  |
| `Short` | `1` |  |
| `Long` | `2` |  |
| `Full` | `3` |  |

## `ListFormatType`

Conjunction = "A, B, and C"; Disjunction = "A, B, or C". Used by IntlAccessor.FormatList().

```csharp
public enum ListFormatType
```

Namespace: `Microsoft.UI.Reactor.Localization`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Conjunction` | `0` |  |
| `Disjunction` | `1` |  |

## `NumberStyle`

Options for IntlAccessor.FormatNumber(). Mirrors ICU/Intl.NumberFormat options.

```csharp
public enum NumberStyle
```

Namespace: `Microsoft.UI.Reactor.Localization`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Default` | `0` |  |
| `Currency` | `1` |  |
| `Percent` | `2` |  |
