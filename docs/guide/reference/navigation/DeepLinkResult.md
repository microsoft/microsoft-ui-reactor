# DeepLinkResult\<TRoute\>

Result of a deep link resolution attempt.

```csharp
public readonly struct DeepLinkResult<TRoute>
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](../../navigation.md)

## Properties

### `Matched`

True if the URI matched a registered pattern.

```csharp
public bool Matched  // get, init
```

### `Routes`

The matched routes (current + optional synthetic back stack).

```csharp
public TRoute[] Routes  // get, init
```
