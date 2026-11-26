# MessageCache

Caches MessageFormatter instances per locale. Each formatter internally caches compiled ICU patterns, so we only need one instance per locale.

```csharp
public sealed class MessageCache
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Constructors

### `MessageCache`

```csharp
public MessageCache()
```

## Methods

### `Flush`

#### `Flush()`

Flushes all cached formatters (e.g., on full locale switch).

```csharp
public void Flush()
```

#### `Flush(string)`

Flushes the cached formatter for a specific locale.

```csharp
public void Flush(string locale)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `locale` | `string` |  |

### `Format`

Formats an ICU pattern with the given arguments for the specified locale.

```csharp
public string Format(string locale, string pattern, IDictionary<string, object>? args = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `locale` | `string` |  |  |
| `pattern` | `string` |  |  |
| `args` | `IDictionary<string, object>?` | `null` |  |

### `GetFormatter`

Gets or creates a MessageFormatter for the given locale.

```csharp
public MessageFormatter GetFormatter(string locale)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `locale` | `string` |  |
