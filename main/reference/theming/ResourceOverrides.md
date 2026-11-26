---
title: "ResourceOverrides"
seo:
  description: "Immutable snapshot of per-control resource overrides built by ResourceBuilder. Contains both literal resource values and theme-reactive ThemeRef entries that are re-resolved on theme change."
---

Immutable snapshot of per-control resource overrides built by [`ResourceBuilder`](/microsoft-ui-reactor/main/reference/theming/ResourceBuilder). Contains both literal resource values and theme-reactive [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef) entries that are re-resolved on theme change.

```csharp
public sealed record ResourceOverrides(IReadOnlyDictionary<string, object> Literals, IReadOnlyDictionary<string, ThemeRef> ThemeRefs)
```

Namespace: `Microsoft.UI.Reactor.Elements`.

**Learn more:** [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## Constructors [#constructors]

### `ResourceOverrides` [#resourceoverrides_1]

Immutable snapshot of per-control resource overrides built by [`ResourceBuilder`](/microsoft-ui-reactor/main/reference/theming/ResourceBuilder). Contains both literal resource values and theme-reactive [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef) entries that are re-resolved on theme change.

```csharp
public ResourceOverrides(IReadOnlyDictionary<string, object> Literals, IReadOnlyDictionary<string, ThemeRef> ThemeRefs)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Literals` | `IReadOnlyDictionary<string, object>` |  |
| `ThemeRefs` | `IReadOnlyDictionary<string, ThemeRef>` |  |

## Properties [#properties]

### `Literals` [#literals]

```csharp
public IReadOnlyDictionary<string, object> Literals  // get, init
```

### `ThemeRefs` [#themerefs]

```csharp
public IReadOnlyDictionary<string, ThemeRef> ThemeRefs  // get, init
```
