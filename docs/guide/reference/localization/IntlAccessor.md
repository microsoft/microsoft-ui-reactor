# IntlAccessor

Provides locale-aware message formatting, number/date formatting, and text direction for the active locale. Obtained via UseIntl() hook.

```csharp
public sealed class IntlAccessor
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Constructors

### `IntlAccessor`

```csharp
public IntlAccessor(string locale, IStringResourceProvider resourceProvider, MessageCache messageCache, string defaultLocale = "en-US", bool pseudoLocalize = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `locale` | `string` |  |  |
| `resourceProvider` | [`IStringResourceProvider`](IStringResourceProvider.md) |  |  |
| `messageCache` | [`MessageCache`](MessageCache.md) |  |  |
| `defaultLocale` | `string` | `"en-US"` |  |
| `pseudoLocalize` | `bool` | `false` |  |

## Properties

### `Direction`

Current text direction.

```csharp
public FlowDirection Direction  // get
```

### `IsRtl`

True if the current locale is right-to-left.

```csharp
public bool IsRtl  // get
```

### `Locale`

Current locale (e.g., "en-US", "ar-SA").

```csharp
public string Locale  // get
```

## Methods

### `Asset`

Resolves a locale-qualified asset path. Falls back to the unqualified path if no locale-specific asset exists.

```csharp
public string Asset(string path)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` | The unqualified asset path (e.g., "Assets/hero-banner.png"). |

**Returns:** The locale-qualified path (e.g., "Assets/en-US/hero-banner.png") if it exists, otherwise the original unqualified path.

### `FormatDate`

Formats a date for the current locale.

```csharp
public string FormatDate(DateTimeOffset value, DateFormatOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `DateTimeOffset` |  |  |
| `options` | [`DateFormatOptions?`](DateFormatOptions.md) | `null` |  |

### `FormatList`

Formats a list of strings with locale-aware joining (e.g., "A, B, and C").

```csharp
public string FormatList(IEnumerable<string> values, ListFormatType type = ListFormatType.Conjunction)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `values` | `IEnumerable<string>` |  |  |
| `type` | [`ListFormatType`](enums.md#listformattype) | `ListFormatType.Conjunction` |  |

### `FormatNumber`

Formats a number for the current locale.

```csharp
public string FormatNumber(double value, NumberFormatOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `double` |  |  |
| `options` | [`NumberFormatOptions?`](NumberFormatOptions.md) | `null` |  |

### `Message`

#### `Message(MessageKey, IDictionary<string, object>?)`

Loads a string resource by key, then formats it with ICU MessageFormat. Falls back to the default locale if the key is missing in the current locale.

```csharp
public string Message(MessageKey key, IDictionary<string, object>? args = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`MessageKey`](MessageKey.md) |  |  |
| `args` | `IDictionary<string, object>?` | `null` |  |

#### `Message(MessageKey, (string Name, object Value), (string Name, object Value)[])`

Compact tuple-args overload for the common case of formatting a single message with a handful of ICU placeholders. Builds a plain [`Dictionary`](https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2) in-place — no reflection, AOT-safe.

```csharp
public string Message(MessageKey key, (string Name, object Value) arg1, params (string Name, object Value)[] more)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`MessageKey`](MessageKey.md) |  |
| `arg1` | `(string Name, object Value)` |  |
| `more` | `(string Name, object Value)[]` |  |

**Remarks**

Tuples with a null `Value` are dropped before the dictionary is built — this matches the behavior of the prior reflection path, which skipped null-valued properties so the formatter saw the placeholder as "missing" rather than substituting an empty string.

**Example**

```csharp
t.Message(Greeting, ("name", "World"));
t.Message(SearchResults, ("count", 0), ("query", "test"));
```

### `RichMessage`

#### `RichMessage(MessageKey, IDictionary<string, object>?, Dictionary<string, Func<string, Element>>?)`

Formats a message that contains rich text tags (e.g., \<bold\>text\</bold\>), mapping each tag to an element factory. Returns a GroupElement containing the resulting child elements (text spans + wrapped elements).

```csharp
public Element RichMessage(MessageKey key, IDictionary<string, object>? args = null, Dictionary<string, Func<string, Element>>? tags = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`MessageKey`](MessageKey.md) |  |  |
| `args` | `IDictionary<string, object>?` | `null` |  |
| `tags` | `Dictionary<string, Func<string, Element>>?` | `null` |  |

**Remarks**

The .resw value uses XML-like tags: "Click \<link\>here\</link\> to read the \<bold\>docs\</bold\>." Tags are mapped via the `tags` dictionary. Unrecognized tags are rendered as plain text (tag markers stripped). Nested tags are not supported — only the outermost tag is processed.

#### `RichMessage(MessageKey, (string Name, object Value), (string Name, object Value)[])`

Compact tuple-args overload of the dict-based `RichMessage` — same allocation profile as the tuple-args `Message` overload. Tags must be supplied via the dict-based overload; this variant is for the common case where the developer just wants the formatted text. Null-valued tuples are dropped (see the tuple-args `Message` remarks).

```csharp
public Element RichMessage(MessageKey key, (string Name, object Value) arg1, params (string Name, object Value)[] more)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`MessageKey`](MessageKey.md) |  |
| `arg1` | `(string Name, object Value)` |  |
| `more` | `(string Name, object Value)[]` |  |
