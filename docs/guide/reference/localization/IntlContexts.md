# IntlContexts

Context-based locale context. The LocaleProvider component provides an IntlAccessor to its subtree via this context. Components consume it via UseIntl() or UseContext(IntlContexts.Locale).

```csharp
public static class IntlContexts
```

Namespace: `Microsoft.UI.Reactor.Localization`.

**Learn more:** [Localization](../../localization.md)

## Fields

### `Locale`

The Context for locale/intl. Default is null — UseIntl() falls back to OS locale.

```csharp
public static readonly Context<IntlAccessor?> Locale
```
