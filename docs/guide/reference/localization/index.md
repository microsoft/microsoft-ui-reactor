# Localization

Message keys, string resources, number and date formatting, and right-to-left helpers.

**Learn more:** [Localization](../../localization.md)

## Types

| Type | Description |
| --- | --- |
| [Enums](enums.md) | The enumerations in this category. |
| [`DateFormatOptions`](DateFormatOptions.md) |  |
| [`IntlAccessor`](IntlAccessor.md) | Provides locale-aware message formatting, number/date formatting, and text direction for the active locale. |
| [`IntlContexts`](IntlContexts.md) | Context-based locale context. |
| [`IStringResourceProvider`](IStringResourceProvider.md) | Abstracts string resource loading so that the localization system can be tested without MRT/ResourceLoader. |
| [`MessageCache`](MessageCache.md) | Caches MessageFormatter instances per locale. |
| [`MessageKey`](MessageKey.md) | Identifies a localizable message by namespace and key. |
| [`NumberFormatOptions`](NumberFormatOptions.md) |  |
| [`PseudoLocalizer`](PseudoLocalizer.md) | Transforms strings for pseudolocalization testing: - Replaces ASCII chars with accented equivalents - Wraps in \[!! ... !!\] markers - Pads ~30% longer to simulate text expansion - Preserves ICU syntax (\{variables\}, \{count, plural, ...\}) untouched |
| [`ReswResourceProvider`](ReswResourceProvider.md) | Production implementation of `IStringResourceProvider` that reads .resw files directly from disk as XML. |
| [`RtlHelper`](RtlHelper.md) | Determines text direction for a locale based on CLDR data. |
