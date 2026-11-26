# ReswResourceProvider

Production implementation of [`IStringResourceProvider`](IStringResourceProvider.md) that reads .resw files directly from disk as XML.

NOTE: This bypasses MRT (Modern Resource Technology) and the Windows PRI resource system. The standard approach would be to use ResourceLoader from Microsoft.global::Windows.ApplicationModel.Resources, which relies on the .resw files being compiled into a .pri (Package Resource Index) at build time. However, ResourceLoader has significant friction in unpackaged app scenarios:

\- ResourceLoader(subtreeName) fails with FileNotFoundException for named .resw files in unpackaged apps, even when the PRI contains the resources. - MRT locale negotiation uses the OS language settings, not the app's explicit locale choice, which conflicts with Reactor's LocaleProvider model where the app controls the active locale at runtime.

By reading .resw XML directly, we get: - Identical behavior for packaged and unpackaged apps - Locale resolution driven by LocaleProvider, not the OS - No dependency on MRT initialization or PRI packaging

REVISIT: If Reactor apps move to packaged deployment (MSIX) as the default, or if MRT Core improves unpackaged support, consider switching back to ResourceLoader for better OS integration (e.g., per-language resource packs, system-level font/layout negotiation).

```csharp
public sealed class ReswResourceProvider : IStringResourceProvider
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Constructors

### `ReswResourceProvider`

Creates a ReswResourceProvider that reads .resw files from the Strings/ folder relative to the application base directory.

```csharp
public ReswResourceProvider(string defaultLocale = "en-US", string? stringsBasePath = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `defaultLocale` | `string` | `"en-US"` | The default/source locale (e.g., "en-US"). |
| `stringsBasePath` | `string?` | `null` | Absolute path to the Strings folder, or null to auto-detect from the application base directory. REVISIT: This auto-detection assumes Strings/ is next to the exe, which may not hold for all deployment layouts. |

## Methods

### `GetString`

```csharp
public string? GetString(string locale, string ns, string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `locale` | `string` |  |
| `ns` | `string` |  |
| `key` | `string` |  |
