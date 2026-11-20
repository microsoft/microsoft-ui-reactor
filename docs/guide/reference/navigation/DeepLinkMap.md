# DeepLinkMap\<TRoute\>

Maps URI patterns to route constructors for deep linking. Patterns use `/segment/{param:type}` syntax where type is `int` or `string`.

```csharp
public sealed class DeepLinkMap<TRoute>
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](../../navigation.md)

## Constructors

### `DeepLinkMap`

```csharp
public DeepLinkMap()
```

## Methods

### `Map`

```csharp
public DeepLinkMap<TRoute> Map(string pattern, Func<RouteArgs, TRoute> factory)
public DeepLinkMap<TRoute> Map(string pattern, Func<RouteArgs, TRoute> factory, Func<TRoute[]> backStackFactory)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `pattern` | `string` |  |
| `factory` | `Func<RouteArgs, TRoute>` |  |
| `backStackFactory` | `Func<TRoute[]>` |  |

### `Resolve`

```csharp
public DeepLinkResult<TRoute> Resolve(string path)
public DeepLinkResult<TRoute> Resolve(Uri uri)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` |  |
| `uri` | `Uri` |  |
