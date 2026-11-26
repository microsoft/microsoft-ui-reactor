# IStringResourceProvider

Abstracts string resource loading so that the localization system can be tested without MRT/ResourceLoader.

```csharp
public interface IStringResourceProvider
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Methods

### `GetString`

Gets a string resource for the given namespace and key in the specified locale. Returns null if the key is not found for that locale.

```csharp
public string? GetString(string locale, string ns, string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `locale` | `string` |  |
| `ns` | `string` |  |
| `key` | `string` |  |
