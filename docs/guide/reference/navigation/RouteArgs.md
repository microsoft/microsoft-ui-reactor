# RouteArgs

Provides typed access to parameters extracted from a URI pattern match.

```csharp
public sealed class RouteArgs
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](../../navigation.md)

## Methods

### `Get`

Gets a parameter value by name, converted to the specified type. Supports `int`, `long`, `bool`, `Guid`, and `string`.

```csharp
public T Get<T>(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### `GetOrDefault`

Gets an optional parameter value. Returns `defaultValue` if the parameter was not present in the matched URI.

```csharp
public T GetOrDefault<T>(string name, T defaultValue = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `defaultValue` | `T` | `default` |  |

### `GetString`

Returns the raw string value of a parameter, or null if not found.

```csharp
public string? GetString(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### `GetWildcard`

Gets a wildcard/catch-all segment value captured by a `**` pattern.

```csharp
public string? GetWildcard()
```

### `Query`

Gets a query string parameter value by name, converted to the specified type. Returns `defaultValue` if the parameter is not present.

```csharp
public T Query<T>(string name, T defaultValue = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `defaultValue` | `T` | `default` |  |

### `QueryString`

Returns the raw query string parameter value, or null if not found.

```csharp
public string? QueryString(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |
