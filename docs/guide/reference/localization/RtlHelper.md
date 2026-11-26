# RtlHelper

Determines text direction for a locale based on CLDR data.

```csharp
public static class RtlHelper
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Methods

### `IsRtlLocale`

Returns true if the given locale uses right-to-left script. Handles both bare language codes ("ar") and full BCP 47 tags ("ar-SA").

```csharp
public static bool IsRtlLocale(string locale)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `locale` | `string` |  |
